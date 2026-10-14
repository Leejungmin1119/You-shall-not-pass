import http from 'node:http';
import { createHmac, randomBytes } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { WebSocketServer, WebSocket } from 'ws';

// 1 auth JSON, 2 ready, 3 peer joined, 4 data + uint32LE peer,
// 5 peer left, 6 kick + peer, 7 lock, 8 heartbeat, 9 error UTF8.
export function createRelay({ maxRooms = 100, maxSockets = 500, maxGuests = 3 } = {}) {
  const rooms = new Map(), attempts = new Map(), key = randomBytes(32);
  const server = http.createServer((req, res) => {
    res.writeHead(req.url === '/health' ? 200 : 404, { 'Content-Type': 'text/plain' });
    res.end(req.url === '/health' ? 'ok' : 'not found');
  });
  const wss = new WebSocketServer({ server, path: '/relay', maxPayload: 65541, perMessageDeflate: false });
  const send = (ws, payload) => {
    if (ws?.readyState !== WebSocket.OPEN) return;
    if (ws.bufferedAmount > 2 * 1024 * 1024) { ws.terminate(); return; }
    ws.send(payload, { binary: true });
  };
  const frame = (type, id) => { const b = Buffer.alloc(5); b[0] = type; b.writeUInt32LE(id, 1); return b; };
  const reject = (ws, reason) => { send(ws, Buffer.concat([Buffer.from([9]), Buffer.from(reason)])); ws.close(1008); };
  wss.on('connection', (ws, req) => {
    if (wss.clients.size > maxSockets) return reject(ws, 'Server busy. Try again later.');
    ws.lastSeen = Date.now();
    ws.on('error', () => {});
    const ip = req.socket.remoteAddress;
    const timeout = setTimeout(() => reject(ws, 'Connection timed out.'), 10000);
    let bytes = 0, messages = 0, windowAt = Date.now();
    ws.on('message', (data, binary) => {
      ws.lastSeen = Date.now();
      if (Date.now() - windowAt > 1000) { bytes = 0; messages = 0; windowAt = Date.now(); }
      bytes += data.length; messages++;
      if (bytes > 1024 * 1024 || messages > 1000) return reject(ws, 'Traffic limit exceeded.');
      if (!binary || data.length < 1) return reject(ws, 'Invalid message.');
      if (!ws.room) {
        if (data[0] !== 1 || data.length > 512) return reject(ws, 'Invalid connection request.');
        let auth;
        try { auth = JSON.parse(data.subarray(1).toString('utf8')); }
        catch { return reject(ws, 'Invalid connection request.'); }
        const now = Date.now(), history = (attempts.get(ip) ?? []).filter(t => now - t < 60000);
        history.push(now); attempts.set(ip, history);
        if (history.length > 30) return reject(ws, 'Too many attempts. Wait a minute.');
        if (auth.version !== 1 || !['host', 'join'].includes(auth.mode) ||
            typeof auth.password !== 'string' || auth.password.length < 8 || auth.password.length > 64 ||
            Buffer.byteLength(auth.password) > 256) return reject(ws, 'Use a password with 8-64 characters.');
        const roomKey = createHmac('sha256', key).update(auth.password).digest('hex');
        if (auth.mode === 'host') {
          if (rooms.has(roomKey)) return reject(ws, 'This password is already in use. Choose another.');
          if (rooms.size >= maxRooms) return reject(ws, 'Server busy. Try again later.');
          const room = { key: roomKey, host: ws, guests: new Map(), nextId: 1, locked: false };
          rooms.set(roomKey, room); ws.room = room; ws.isHost = true;
          clearTimeout(timeout); send(ws, Buffer.from([2]));
        } else {
          const room = rooms.get(roomKey);
          if (!room || room.host.readyState !== WebSocket.OPEN) return reject(ws, 'No room matches this password.');
          if (room.locked) return reject(ws, 'The game has already started.');
          if (room.guests.size >= maxGuests) return reject(ws, 'The room is full.');
          ws.room = room; ws.peer = room.nextId++; room.guests.set(ws.peer, ws);
          clearTimeout(timeout);
          send(room.host, frame(3, ws.peer)); send(ws, Buffer.from([2]));
        }
        return;
      }
      const room = ws.room;
      if (data[0] === 8 && data.length === 1) return send(ws, data);
      if (data[0] === 4 && data.length > 5) {
        if (ws.isHost) send(room.guests.get(data.readUInt32LE(1)), data);
        else { const forwarded = Buffer.from(data); forwarded.writeUInt32LE(ws.peer, 1); send(room.host, forwarded); }
      } else if (ws.isHost && data[0] === 6 && data.length === 5) {
        room.guests.get(data.readUInt32LE(1))?.close(1000);
      } else if (ws.isHost && data[0] === 7 && data.length === 1) room.locked = true;
      else reject(ws, 'Invalid message.');
    });
    ws.on('close', () => {
      clearTimeout(timeout);
      const room = ws.room;
      if (!room) return;
      if (ws.isHost) {
        rooms.delete(room.key);
        for (const guest of room.guests.values()) { send(guest, Buffer.concat([Buffer.from([9]), Buffer.from('The host left the room.')])); guest.close(1000); }
        room.guests.clear();
      } else { room.guests.delete(ws.peer); send(room.host, frame(5, ws.peer)); }
    });
  });
  const timer = setInterval(() => {
    for (const ws of wss.clients) {
      if (Date.now() - ws.lastSeen > 30000) ws.terminate();
    }
    for (const [ip, times] of attempts) if (Date.now() - times.at(-1) > 60000) attempts.delete(ip);
  }, 15000);
  return {
    server,
    async close() { clearInterval(timer); for (const ws of wss.clients) ws.terminate(); await new Promise(r => wss.close(r)); await new Promise(r => server.close(r)); }
  };
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const relay = createRelay();
  relay.server.listen(Number(process.env.PORT ?? 8080), '0.0.0.0', () => console.log('Password relay listening.'));
  process.on('SIGTERM', async () => { await relay.close(); process.exit(0); });
}
