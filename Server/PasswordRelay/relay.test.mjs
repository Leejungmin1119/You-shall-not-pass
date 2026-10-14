import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { WebSocket } from 'ws';
import { createRelay } from './server.mjs';

async function setup(t) {
  const relay = createRelay(); relay.server.listen(0, '127.0.0.1'); await once(relay.server, 'listening');
  t.after(() => relay.close());
  return `ws://127.0.0.1:${relay.server.address().port}/relay`;
}
async function peer(url) {
  const ws = new WebSocket(url), queue = [], waiters = [];
  ws.on('message', b => { queue.push(Buffer.from(b)); for (const notify of [...waiters]) notify(); });
  ws.on('error', () => {});
  await once(ws, 'open');
  return {
    ws,
    send: b => ws.send(b),
    auth: (mode, password = 'test-password-01') => ws.send(Buffer.concat([Buffer.from([1]), Buffer.from(JSON.stringify({ version: 1, mode, password }))])),
    next(type) {
      return new Promise((resolve, reject) => {
        const timer = setTimeout(() => { remove(); reject(new Error(`Missing frame ${type}`)); }, 2000);
        const remove = () => { clearTimeout(timer); const i = waiters.indexOf(check); if (i >= 0) waiters.splice(i, 1); };
        const check = () => { const i = queue.findIndex(b => b[0] === type); if (i >= 0) { const [b] = queue.splice(i, 1); remove(); resolve(b); } };
        waiters.push(check); check();
      });
    }
  };
}
function data(id, text, type = 4) { const b = Buffer.alloc(5); b[0] = type; b.writeUInt32LE(id, 1); return Buffer.concat([b, Buffer.from(text)]); }

test('password creates room; both directions forward with relay assigned identity', async t => {
  const url = await setup(t), host = await peer(url); host.auth('host'); await host.next(2);
  const guest = await peer(url); guest.auth('join'); await guest.next(2);
  const id = (await host.next(3)).readUInt32LE(1);
  guest.send(data(999, 'hello')); const received = await host.next(4);
  assert.equal(received.readUInt32LE(1), id); assert.equal(received.subarray(5).toString(), 'hello');
  host.send(data(id, 'world')); assert.equal((await guest.next(4)).subarray(5).toString(), 'world');
  const closed = once(guest.ws, 'close'); guest.ws.close(); await closed;
  assert.equal((await host.next(5)).readUInt32LE(1), id);
});
test('wrong password, duplicate password and weak password are rejected', async t => {
  const url = await setup(t), host = await peer(url); host.auth('host'); await host.next(2);
  const wrong = await peer(url); wrong.auth('join', 'wrong-password'); assert.match((await wrong.next(9)).toString(), /No room/);
  const duplicate = await peer(url); duplicate.auth('host'); assert.match((await duplicate.next(9)).toString(), /already in use/);
  const weak = await peer(url); weak.auth('host', '123'); assert.match((await weak.next(9)).toString(), /8-64/);
});
test('room has three guests plus host; started room rejects new joins', async t => {
  const url = await setup(t), host = await peer(url); host.auth('host'); await host.next(2);
  for (let i = 0; i < 3; i++) { const g = await peer(url); g.auth('join'); await g.next(2); await host.next(3); }
  const fourth = await peer(url); fourth.auth('join'); assert.match((await fourth.next(9)).toString(), /full/);
  host.send(Buffer.from([7])); host.send(Buffer.from([8])); await host.next(8);
  const late = await peer(url); late.auth('join'); assert.match((await late.next(9)).toString(), /already started/);
});
test('host loss notifies guests and removes password room', async t => {
  const url = await setup(t), host = await peer(url); host.auth('host'); await host.next(2);
  const guest = await peer(url); guest.auth('join'); await guest.next(2); await host.next(3);
  host.ws.close(); assert.match((await guest.next(9)).toString(), /host left/);
  const replacement = await peer(url); replacement.auth('host'); await replacement.next(2);
});
test('malformed auth and text frames cannot enter a room; heartbeat works', async t => {
  const url = await setup(t);
  const malformed = await peer(url); malformed.send(Buffer.from([1, 123])); await malformed.next(9);
  const text = await peer(url); text.send('password'); await text.next(9);
  const host = await peer(url); host.auth('host'); await host.next(2);
  host.send(Buffer.from([8])); assert.deepEqual(await host.next(8), Buffer.from([8]));
});
test('separate rooms are isolated and a host can kick its own guest', async t => {
  const url = await setup(t);
  const a = await peer(url); a.auth('host', 'password-room-a'); await a.next(2);
  const b = await peer(url); b.auth('host', 'password-room-b'); await b.next(2);
  const ga = await peer(url); ga.auth('join', 'password-room-a'); await ga.next(2); const aid = (await a.next(3)).readUInt32LE(1);
  const gb = await peer(url); gb.auth('join', 'password-room-b'); await gb.next(2); const bid = (await b.next(3)).readUInt32LE(1);
  ga.send(data(bid, 'A')); assert.equal((await a.next(4)).subarray(5).toString(), 'A');
  gb.send(data(aid, 'B')); assert.equal((await b.next(4)).subarray(5).toString(), 'B');
  const closed = once(ga.ws, 'close'); a.send(data(aid, '', 6)); await closed; await a.next(5);
  gb.send(data(bid, 'still connected')); assert.equal((await b.next(4)).subarray(5).toString(), 'still connected');
});
