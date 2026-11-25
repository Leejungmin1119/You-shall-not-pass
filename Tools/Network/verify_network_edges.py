#!/usr/bin/env python3
"""Real KCP checks with explicit server fixtures for death and match-end branches."""
import argparse
import json
from pathlib import Path
import subprocess
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


def read(peer):
    try:
        snapshot = json.loads((report / (peer + '.json')).read_text())
        if snapshot.get('serverRegistryValid') is False:
            raise AssertionError('Server player registry diverged on ' + peer)
        return snapshot
    except (OSError, ValueError):
        return {}


def wait(predicate, label, timeout=35):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        if any(process.poll() is not None for process in processes.values()):
            raise AssertionError('Player process exited during ' + label)
        time.sleep(0.05)
    raise AssertionError('Timed out during ' + label)


def check(condition, label):
    if not condition:
        raise AssertionError(label)
    passed.append(label)
    print('PASS ' + label, flush=True)


def command(peer, action, timeout=35, **values):
    sequences[peer] += 1
    path = report / (peer + '.command.json')
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(dict(sequence=sequences[peer], action=action, **values)))
    temporary.replace(path)
    wait(lambda: read(peer).get('lastCommand') == sequences[peer], action, timeout)


def roster(count, peers):
    wait(lambda: all(len(read(peer).get('players', [])) == count for peer in peers), 'roster')


def start_game():
    for peer in ['host', 'c1']:
        command(peer, 'ready', ready=True)
    wait(lambda: all(read(peer).get('racePhase') == 'Racing' for peer in ['host', 'c1']), 'start racing', 55)


def finish_with_fixture(action, reason):
    command('host', action)
    endings = {}

    def returned():
        for peer in ['host', 'c1']:
            snapshot = read(peer)
            if snapshot.get('racePhase') == 'Ended':
                endings[peer] = snapshot
        return len(endings) == 2 and all(read(peer).get('screen') == 'Room' for peer in ['host', 'c1'])

    wait(returned, reason + ' result and return', 20)
    check(all(s['endReason'] == reason for s in endings.values()), reason + ' reaches both peers')
    check(endings['host']['winners'] == endings['c1']['winners'], reason + ' winners replicate')
    scores = {p['id']: p['score'] for p in endings['host']['gamePlayers']}
    expected = sorted(player for player, score in scores.items() if score == max(scores.values()))
    check(sorted(endings['host']['winners']) == expected, reason + ' selects highest scoring players')
    check(all(not p['ready'] for p in read('host')['players']), reason + ' returns with readiness reset')
    result[reason] = endings


try:
    for peer in ['host', 'c1', 'c2']:
        sequences[peer] = 0
        for suffix in ['.json', '.command.json', '.events.jsonl']:
            (report / (peer + suffix)).unlink(missing_ok=True)
        parameters = [str(args.player.resolve()), '-batchmode', '-nographics',
                      '-lobbyProbeDirectory', str(report), '-lobbyProbeId', peer,
                      '-logFile', str(report / (peer + '-player.log'))]
        if peer != 'c1':
            parameters += ['-lobbyProbeManual']
        logs[peer] = (report / (peer + '.log')).open('w')
        processes[peer] = subprocess.Popen(parameters, stdout=logs[peer], stderr=subprocess.STDOUT)
        wait(lambda: bool(read(peer)), peer + ' launch', 45)
        if peer == 'host':
            command(peer, 'host')
        if peer != 'c2':
            wait(lambda: read(peer).get('screen') == 'Room', peer + ' room')
    roster(2, ['host', 'c1'])
    command('host', 'character', character=0)
    command('c1', 'character', character=2)
    client_id = read('c1')['localPlayerId']
    wait(lambda: next(p for p in read('host')['players'] if p['netId'] == client_id)['character'] == 2,
         'character replication')
    check(True, 'remote character selection replicates')
    command('c1', 'rawCharacter', character=99)
    command('c1', 'ready', ready=True)
    command('c1', 'rawCharacter', character=0)
    time.sleep(0.3)
    check(next(p for p in read('host')['players'] if p['netId'] == client_id)['character'] == 2,
          'server rejects invalid and ready-locked character changes')
    start_game()
    check(all(sorted(p['character'] for p in read(peer)['gamePlayers']) == [0, 2] for peer in ['host', 'c1']),
          'selected characters carry into gameplay on both peers')
    turn = read('host')['turn']
    command('c2', 'direct', name='127.0.0.1')
    wait(lambda: bool(read('c2').get('connectionError')) and not read('c2')['connected'], 'late join rejected')
    time.sleep(1.2)
    check(all(read(peer)['scene'] == 'Stage1' and read(peer)['racePhase'] == 'Racing' and read(peer)['turn'] == turn
              for peer in ['host', 'c1']), 'rejected late join never aborts the existing match')

    defender = next(peer for peer in ['host', 'c1']
                    if next(p['id'] for p in read(peer)['gamePlayers'] if p['local']) != read('host')['attacker'])
    owner = next(p['id'] for p in read(defender)['gamePlayers'] if p['local'])
    command(defender, 'buyMissile', timeout=90)
    wait(lambda: next(p for p in read(defender)['gamePlayers'] if p['local'])['count'] == 1, 'missile purchase')
    command(defender, 'useAt', x=0.6, y=0.7)
    wait(lambda: any(m['owner'] == owner for m in read('host')['missiles']), 'missile spawn')
    missile = next(m for m in read('host')['missiles'] if m['owner'] == owner)
    target = dict(x=missile['aimX'] + 0.4, y=missile['aimY'] + 0.4)
    command(defender, 'aim', roundOffset=-1, **target)
    time.sleep(0.22)
    current = next(m for m in read('host')['missiles'] if m['id'] == missile['id'])
    check(abs(current['aimX'] - missile['aimX']) < 0.001 and abs(current['aimY'] - missile['aimY']) < 0.001,
          'previous-turn aim cannot redirect the current missile')
    command(defender, 'aim', **target)
    wait(lambda: any(m['id'] == missile['id'] and abs(m['aimX'] - target['x']) < 0.001
                     for m in read('host')['missiles']), 'valid aim')
    check(True, 'current-turn aim still redirects the owned missile')

    command('host', 'deathFixture')
    wait(lambda: all(read(peer)['racePhase'] == 'DeathPresentation' for peer in ['host', 'c1']), 'death presentation')
    wait(lambda: all(read(peer)['uiHidden'] and read(peer)['timeScale'] < 1 for peer in ['host', 'c1']), 'death globals')
    command('c1', 'leave')
    wait(lambda: read('host')['screen'] == 'Room', 'return after death disconnect')
    check(read('host')['timeScale'] == 1 and not read('host')['uiHidden'],
          'death disconnect restores time scale and lobby UI')
    check(read('host')['advertising'], 'death disconnect restores room discovery')
    command('c1', 'direct', name='127.0.0.1')
    roster(2, ['host', 'c1'])
    start_game()
    finish_with_fixture('timeLimitFixture', 'TimeLimit')
    check(len(result['TimeLimit']['host']['winners']) == 2, 'time-limit tie preserves both winners')
    start_game()
    finish_with_fixture('scoreLimitFixture', 'ScoreLimit')
    check(max(p['score'] for p in result['ScoreLimit']['host']['gamePlayers']) == 20,
          'score-limit path keeps the 20-point rule')
    command('c2', 'searchMultipleFixture')
    wait(lambda: '여러 개' in read('c2').get('connectionError', ''), 'ambiguous discovery notice')
    check(not read('c2')['searching'] and not read('c2')['connected'], 'ambiguous discovery stops with direct-IP guidance')
    check(all(not read(peer)['errors'] for peer in processes), 'no runtime errors in edge-case checks')
    result['success'] = True
    print('ALL ' + str(len(passed)) + ' EDGE CHECKS PASSED', flush=True)
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
