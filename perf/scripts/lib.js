// Shared helpers for the HRM perf (k6) scenarios.
// Parameterized by env vars (k6 -e KEY=VAL):
//   BASE_URL   default http://localhost:5000
//   SUBDOMAIN  default perf
//   EMAIL      default perfadmin@perf.test   (the pool ADMIN; pool users are perfuserNNN@perf.test)
//   PASSWORD   default Admin@123!
//   USER_POOL  default 30    — how many seeded perf users the VUs spread across (ISSUE-534)
//   PIN_USER   unset|1       — 1 pins every VU to perfadmin, reproducing the ISSUE-534 defect on purpose
//
// ===== ISSUE-534: why this file owns a USER POOL =====================================================
// Program.cs installs a GLOBAL fixed-window limiter — PermitLimit=300, Window=1min, QueueLimit=0 —
// partitioned by `t:{tenantId}:u:{userId}` (HRM.Api/RateLimiting/GlobalRateLimitPartition.cs). Every VU
// used to authenticate as the SAME perfadmin user, so an entire run shared ONE partition and everything
// past 300 req/min came back 429 in ~2ms.
//
// Fast 429s pull p95 DOWN. So the scripts printed a green `p(95) < SLA` that was the p95 of BEING
// RATE-LIMITED, not the p95 of the endpoint. Observed on a real run: 26,182 requests, 901 successes —
// 300 x 3 windows, the limiter's arithmetic rather than the application's. It nearly shipped as a pass.
// Re-confirmed against the running stack 2026-09-07: 340 sequential single-user GETs -> exactly 300x200
// then 40x429.
//
// THREE constraints bound the fix, and they interact:
//   1. GLOBAL limiter: 300 req/min per (tenant, user)  -> spread VUs over N users.
//   2. `auth-login` policy: 10 req/min per CLIENT IP on POST /api/v1/auth/login (AuthController:42)
//      -> minting N tokens from one k6 host costs ~ceil(N/9) minutes.
//   3. Access tokens live 15 minutes (JwtService.GenerateAccessToken -> AddMinutes(15)).
// (2) + (3) put a HARD CEILING on the pool: mint_minutes + run_minutes must stay under 15. So the pool
// cannot simply be made "big enough" for an unpaced 50-VU script — that needs ~150 users, i.e. ~17 min of
// minting, by which time the first token is already dead. Throughput is therefore PACED to a
// deterministic ceiling (see `pace`) and the pool is sized to that ceiling. Every script header shows its
// own arithmetic, and `assertBudget` re-derives it at runtime and REFUSES to run an illegal config.
// =====================================================================================================
import http from 'k6/http';
import { check, sleep } from 'k6';

export const BASE = __ENV.BASE_URL || 'http://localhost:5000';
export const SUBDOMAIN = __ENV.SUBDOMAIN || 'perf';
const ADMIN_EMAIL = __ENV.EMAIL || 'perfadmin@perf.test';
const PASSWORD = __ENV.PASSWORD || 'Admin@123!';

/** Mirrors Program.cs GlobalLimiter PermitLimit / Window. If that changes, change this. */
export const LIMIT_PER_MIN = 300;
/** Mirrors the `auth-login` policy PermitLimit (10/min/IP). One permit left spare for retry headroom. */
const LOGINS_PER_WINDOW = parseInt(__ENV.LOGINS_PER_WINDOW || '9', 10);

/** Users seeded by perf/seed/seed-perf-tenant.sql (`-v user_pool=N`). */
export const POOL_SIZE = parseInt(__ENV.USER_POOL || '30', 10);

/** ISSUE-534 regression probe: 1 => every VU shares perfadmin's partition, i.e. the OLD broken behaviour. */
export const PIN_USER = __ENV.PIN_USER === '1';

// dev-mode tenant resolution header (TenantResolutionMiddleware fallback)
export function tenantHeaders(token) {
  const h = { 'X-Tenant-Subdomain': SUBDOMAIN, 'Content-Type': 'application/json' };
  if (token) h['Authorization'] = `Bearer ${token}`;
  return h;
}

export function poolEmail(i) {
  return `perfuser${String(i).padStart(3, '0')}@perf.test`;
}

/** One login -> bearer token. Called from setup() so bcrypt cost is paid once. */
export function login(email) {
  const who = email || ADMIN_EMAIL;
  const res = http.post(
    `${BASE}/api/v1/auth/login`,
    JSON.stringify({ email: who, password: PASSWORD }),
    { headers: tenantHeaders(), tags: { name: 'login' } },
  );
  check(res, { 'login 200': (r) => r.status === 200 });
  const token = res.json('data.accessToken');
  if (!token) {
    throw new Error(
      `login failed for ${who}: ${res.status} ${res.body}` +
      (res.status === 429
        ? ' — auth-login is 10/min/IP; lower LOGINS_PER_WINDOW or wait out the window.'
        : ' — is the perf tenant seeded? see perf/README.md §1.'),
    );
  }
  return token;
}

/**
 * ISSUE-534: mint one token per pool user so the (tenantId, userId) partition actually varies.
 *
 * Paced in batches of LOGINS_PER_WINDOW with a 61s gap, because `auth-login` is a 1-minute FIXED window
 * of 10/IP. Deliberately NOT parallel and NOT retried-on-429: eating 429s here would put the run's own
 * setup into http_req_failed and muddy the very metric this fix exists to protect.
 *
 * Cost: ceil(POOL_SIZE / LOGINS_PER_WINDOW) - 1 minutes. Budget it against the 15-minute token TTL.
 */
export function loginPool() {
  if (PIN_USER) {
    console.warn(
      '[ISSUE-534] PIN_USER=1 — every VU is pinned to ' + ADMIN_EMAIL + ', sharing ONE rate-limit ' +
      'partition. This REPRODUCES the defect; expect http_req_failed to go red. Not a valid perf run.',
    );
    return [login(ADMIN_EMAIL)];
  }
  const tokens = [];
  for (let i = 1; i <= POOL_SIZE; i++) {
    if (i > 1 && (i - 1) % LOGINS_PER_WINDOW === 0) {
      sleep(61); // wait out the auth-login fixed window rather than collect 429s
    }
    tokens.push(login(poolEmail(i)));
  }
  console.log(`[ISSUE-534] minted ${tokens.length} pool tokens (perfuser001..${String(POOL_SIZE).padStart(3, '0')}).`);
  return tokens;
}

/**
 * Which pool user this iteration authenticates as. Deterministic (so two runs are comparable) and EVEN.
 *
 * Pure `__VU % N` is deterministic but UNEVEN whenever N does not divide the VU count: 50 VU over 30
 * users leaves 20 partitions carrying two VUs and 10 carrying one, so the hot partitions see 2x the mean
 * and half the computed budget is unusable. Adding `__ITER` rotates every VU through the whole pool, so
 * aggregate load is even to within one iteration. Random would also spread, but then no two runs are
 * comparable — which is the whole point of a perf baseline.
 */
export function pickToken(tokens) {
  return tokens[(__VU - 1 + __ITER) % tokens.length];
}

/**
 * Hold each iteration to a fixed cadence, so throughput is a stated number instead of an artifact of
 * however fast the server happened to reply. `sleep(0.5)` did not do this: a faster backend produced MORE
 * requests, which is precisely how these scripts drifted over the 300/min ceiling. A floor, not a cap —
 * an iteration slower than `seconds` is simply not padded.
 */
export function pace(startedAtMs, seconds) {
  const elapsed = (Date.now() - startedAtMs) / 1000;
  if (elapsed < seconds) {
    sleep(seconds - elapsed);
  }
}

/**
 * ISSUE-534 prevention half (the `http_req_failed` threshold is the detection half): re-derive the
 * per-partition request rate from the ACTUAL config and refuse to start a run that would measure the
 * limiter instead of the application. Without this, raising VUS or lowering USER_POOL silently
 * reintroduces the exact defect this ticket fixed.
 */
export function assertBudget({ vus, reqsPerIteration, iterationSeconds, poolSize }) {
  const totalPerMin = (vus * reqsPerIteration * 60) / iterationSeconds;
  const perPartition = totalPerMin / poolSize;
  const line =
    `[ISSUE-534] budget: ${vus} VU x ${reqsPerIteration} req / ${iterationSeconds}s over ${poolSize} user(s) ` +
    `= ${Math.round(totalPerMin)} req/min total, ${Math.round(perPartition)} req/min per (tenant,user) ` +
    `partition (limiter: ${LIMIT_PER_MIN}/min).`;

  if (PIN_USER) {
    console.warn(`${line} PIN_USER=1 — breach is EXPECTED, this is the regression probe.`);
    return;
  }
  if (perPartition >= LIMIT_PER_MIN) {
    const need = Math.ceil(totalPerMin / (LIMIT_PER_MIN * 0.8));
    throw new Error(
      `${line} REFUSING TO RUN — this configuration measures the rate limiter, not the application ` +
      `(ISSUE-534). Re-seed with -v user_pool=${need} and run with -e USER_POOL=${need}, or lower VUS.`,
    );
  }
  if (perPartition >= LIMIT_PER_MIN * 0.8) {
    console.warn(`${line} WARNING: over 80% of the limiter budget — little headroom for burstiness.`);
  } else {
    console.log(line);
  }
}
