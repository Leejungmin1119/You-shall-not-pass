#!/usr/bin/env python3
"""Exercise host-only lobby removal through real owned Mirror Commands."""
import argparse
import json
from pathlib import Path
import subprocess
import signal
import time


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--player', type=Path, required=True)
parser.add_argument('--report-dir', type=Path, required=True)
args = parser.parse_args()
report = args.report_dir.resolve()
report.mkdir(parents=True, exist_ok=True)
processes = {}
logs = {}
sequences = {}
passed = []
result = {'success': False}
peers = ['host', 'c1', 'c2', 'c3']


def read(peer):
    try:
        snapshot = json.loads((report / (peer + '.json')).read_text())
        if snapshot.get('serverRegistryValid') is False:
            raise AssertionError('Server player registry diverged on ' + peer)
        return snapshot
    except (OSError, ValueError):
        return {}


def wait(predicate, label, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        if any(process.poll() is not None for process in processes.values()):
            raise AssertionError('Player exited while waiting for ' + label)
        time.sleep(0.1)
    raise AssertionError('Timed out waiting for ' + label)


def check(condition, label):
    if not condition:
        raise AssertionError(label)
    passed.append(label)
    print('PASS ' + label, flush=True)


def command(peer, action, **values):
    sequences[peer] += 1
    value = dict(sequence=sequences[peer], action=action, **values)
    path = report / (peer + '.command.json')
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value))
    temporary.replace(path)
    wait(lambda: read(peer).get('lastCommand') == sequences[peer], action)


def roster(count, remaining):
    wait(lambda: all(len(read(peer).get('players', [])) == count for peer in remaining), 'roster')


def capture(peer, filename):
    path = report / (peer + '-' + read(peer)['screen'] + '-screen.png')
    path.unlink(missing_ok=True)
    command(peer, 'capture')
    wait(lambda: path.exists() and path.stat().st_size > 10000, 'screenshot')
    # CaptureScreenshot finishes writing asynchronously.
    previous_size = 0
    for _ in range(30):
        size = path.stat().st_size
        if size == previous_size:
            break
        previous_size = size
        time.sleep(0.2)
    (report / filename).write_bytes(path.read_bytes())


try:
    for peer in peers:
        sequences[peer] = 0
        for suffix in ['.json', '.command.json', '.events.jsonl']:
            (report / (peer + suffix)).unlink(missing_ok=True)
        parameters = [str(args.player.resolve()), '-lobbyProbeDirectory', str(report),
                      '-lobbyProbeId', peer, '-logFile', str(report / (peer + '-player.log'))]
        if peer in ['host', 'c1']:
            parameters += ['-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
        else:
            parameters += ['-batchmode', '-nographics']
        if peer == 'host':
            parameters += ['-lobbyProbeManual']
        logs[peer] = (report / (peer + '.log')).open('w')
        processes[peer] = subprocess.Popen(parameters, stdout=logs[peer], stderr=subprocess.STDOUT)
        wait(lambda: bool(read(peer)), peer + ' launch', 45)
        if peer == 'host':
            command(peer, 'host')
        wait(lambda: read(peer).get('screen') == 'Room', peer + ' room', 45)
    roster(4, peers)
    host_id = read('host')['localPlayerId']
    target_id = read('c1')['localPlayerId']
    original_seats = {p['netId']: p['seat'] for p in read('host')['players']}
    expected = sorted(read(peer)['localPlayerId'] for peer in peers if peer != 'host')
    check(sorted(read('host')['kickablePlayers']) == expected, 'host can remove all three remote players')
    check(all(not read(peer)['kickablePlayers'] for peer in peers if peer != 'host'),
          'remote clients have no removal controls')
    capture('host', 'host-kick-buttons.png')
    capture('c1', 'client-no-kick-buttons.png')

    command('c2', 'kick', playerId=target_id)
    command('c2', 'kick', playerId=host_id)
    command('host', 'kick', playerId=host_id)
    command('host', 'kick', playerId=4294967295)
    time.sleep(0.7)
    check(all(read(peer)['connected'] for peer in peers), 'unauthorized, self and unknown-target requests are rejected')
    roster(4, peers)

    for peer in peers:
        command(peer, 'ready', ready=True)
    wait(lambda: all(read(peer).get('countdown') for peer in peers), 'ready countdown')
    old_deadline = read('host')['deadline']
    command('host', 'kick', playerId=target_id)
    remaining = ['host', 'c2', 'c3']
    roster(3, remaining)
    wait(lambda: read('c1').get('screen') == 'Main' and not read('c1')['connected'], 'removed client main menu')
    check('방장이 내보냈습니다' in read('c1')['connectionError'], 'removed player receives the correct reason')
    capture('c1', 'removed-client-notice.png')
    wait(lambda: all(not read(peer)['countdown'] and read(peer)['deadline'] == 0 for peer in remaining),
         'countdown cancellation')
    check(all(not player['ready'] for player in read('host')['players']), 'remaining players readiness resets')
    check(all(original_seats[player['netId']] == player['seat'] for player in read('host')['players']),
          'remaining players keep their original seats')
    check(all(read(peer)['connected'] and not read(peer)['connectionError'] for peer in remaining),
          'host and other clients remain connected without failure notices')
    time.sleep(max(0, old_deadline - read('host')['now']) + 0.4)
    check(all(read(peer)['screen'] == 'Room' for peer in remaining), 'cancelled countdown never starts the game')
    capture('host', 'host-after-kick.png')

    command('c1', 'direct', name='127.0.0.1')
    roster(4, peers)
    check(not read('c1')['connectionError'], 'successful rejoin clears the removal notice')
    replacement = next(p for p in read('c1')['players'] if p['local'])
    check(replacement['seat'] == original_seats[target_id], 'rejoining player fills the released seat')
    check(replacement['netId'] != target_id, 'rejoining player receives a new identity')
    command('host', 'kick', playerId=target_id)
    time.sleep(0.3)
    check(read('c1')['connected'], 'stale removal request cannot affect the replacement player')
    command('host', 'kick', playerId=replacement['netId'])
    roster(3, remaining)
    check(True, 'host can remove the rejoined identity')
    command('host', 'kick', playerId=read('c2')['localPlayerId'])
    roster(2, ['host', 'c3'])
    check(read('host')['advertising'], 'room remains available after repeated removals')
    command('c1', 'direct', name='127.0.0.1')
    roster(3, ['host', 'c1', 'c3'])
    check(True, 'other players can join after repeated removals')
    # A stalled client cannot prevent the server from releasing its seat.
    processes['c3'].send_signal(signal.SIGSTOP)
    try:
        command('host', 'kick', playerId=read('c3')['localPlayerId'])
        roster(2, ['host', 'c1'])
    finally:
        processes['c3'].send_signal(signal.SIGCONT)
    check(True, 'server disconnects a nonresponsive client and releases its seat')
    wait(lambda: not read('c3')['connected'], 'stalled client cleanup')
    check(all(not read(peer)['errors'] for peer in peers), 'no runtime errors on any peer')
    result['success'] = True
    print('ALL ' + str(len(passed)) + ' KICK CHECKS PASSED', flush=True)
except Exception as error:
    result['error'] = str(error)
    print('FAILED ' + str(error), flush=True)
finally:
    result.update(passed=passed, snapshots={peer: read(peer) for peer in processes})
    (report / 'result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2))
    for peer, process in processes.items():
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        logs[peer].close()
if not result['success']:
    raise SystemExit(1)
