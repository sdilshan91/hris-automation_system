const signalR = require('@microsoft/signalr');

const BASE = 'http://localhost:5000';
const token = process.env.SR_TOKEN;
const sub = 'fntest';

const conn = new signalR.HubConnectionBuilder()
  .withUrl(`${BASE}/hubs/notifications`, {
    accessTokenFactory: () => token,
    headers: { 'X-Tenant-Subdomain': sub },
    transport: signalR.HttpTransportType.WebSockets,
    skipNegotiation: false,
  })
  .configureLogging(signalR.LogLevel.Warning)
  .build();

let got = false;
conn.on('ReceiveNotification', (msg) => {
  got = true;
  console.log('>>> ReceiveNotification RECEIVED:', JSON.stringify(msg));
});

(async () => {
  try {
    await conn.start();
    console.log('CONNECTED state=', conn.state, 'connectionId=', conn.connectionId);
    const waitMs = parseInt(process.env.SR_WAIT || '20000', 10);
    await new Promise(r => setTimeout(r, waitMs));
    console.log('DELIVERED=', got);
    await conn.stop();
    process.exit(got ? 0 : 7);
  } catch (e) {
    console.error('SR ERROR:', e.message);
    process.exit(2);
  }
})();
