---
id: TC-CHR-325
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: automated
created: 2026-07-05
automated_by: "HRM.Tests/Unit/ClamAvInStreamProtocolTests.cs"
---

# TC-CHR-325: ClamAV INSTREAM virus scanner — wire framing, response parsing, and config-gated DI (ISSUE-101 / NFR-3)

## 1. Test Objective
Verify US-CHR-001 NFR-3 (ISSUE-101): the ClamAV `INSTREAM` protocol seam frames uploads correctly and parses clamd replies, and the scanner is config-gated in DI. The pure `ClamAvInStreamProtocol` writes `zINSTREAM\0` + per-chunk 4-byte BIG-ENDIAN length prefixes (repeating across multiple chunks) + a zero-length terminator; it maps `... OK`→Clean, `{sig} FOUND`→Infected (extracting the signature), and `... ERROR`/garbage/empty→`ClamAvProtocolException` (a scan failure, never a misdetection). The DI gate wires `ClamAvVirusScanner` only when `VirusScanning:ClamAv:Host` is set, otherwise keeps the `AllowWithLogVirusScanner` allow-stub — verified without opening a socket.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirement: NFR-3 (malware scanning of uploads)
- Finding: ISSUE-101

## 3. Preconditions
- None (pure unit + DI-container level; no live clamd daemon, no network).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Payload | 600 bytes, chunkSize 256 | forces 3 chunks (256+256+88) |
| Chunk length 256 | big-endian [00 00 01 00] | byte order observable |
| Replies | `stream: OK`, `stream: Eicar-Test-Signature FOUND`, `... ERROR`, empty | parse cases |
| Config key | `VirusScanning:ClamAv:Host` set / unset | DI gate |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `WriteInStreamAsync` a 600-byte payload at chunkSize 256 | Exact wire bytes: `zINSTREAM\0`, then [00 00 01 00]+256B, [00 00 01 00]+256B, [00 00 00 58]+88B, then [00 00 00 00] terminator. |
| 2 | `ParseResponse("stream: OK")` / with trailing NUL | Clean; ThreatName null. |
| 3 | `ParseResponse("stream: Eicar-Test-Signature FOUND")` | Infected; extracted signature = `Eicar-Test-Signature`. |
| 4 | `ParseResponse` of `... ERROR` / garbage / empty | throws `ClamAvProtocolException`. |
| 5 | `ReadResponseAsync` of a NUL-terminated reply | accumulates the line up to the NUL. |
| 6 | Build infrastructure DI with Host unset, then set | unset → `AllowWithLogVirusScanner`; set → `ClamAvVirusScanner` (no socket opened). |

## 6. Postconditions
- The scanner protocol is contract-pinned and the production wiring is config-gated; unconfigured environments keep the allow-stub.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
