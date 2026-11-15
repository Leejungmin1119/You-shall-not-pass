from pathlib import Path
import argparse
import json
import subprocess
import time
import threading

parser = argparse.ArgumentParser(description='Measure real owned aim Commands and server trap messages with opt-in fixtures.')
parser.add_argument('--player', type=Path, required=True)
parser.add_argument('--report-dir', type=Path, required=True)
parser.add_argument('--baseline', type=Path)
args = parser.parse_args()
report = args.report_dir.resolve()
report.mkdir(parents=True, exist_ok=True)
processes = {}
logs = {}
sequences = {}
traffic_sequences = {}
measurements = []
result = {'success': False}


def read(peer, suffix=''):
    try:
        return json.loads((report / (peer + suffix + '.json')).read_text())
    except (OSError, ValueError):
        return {}


def wait(predicate, label, timeout=45):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        if any(process.poll() is not None for process in processes.values()):
            raise AssertionError('Process exited during ' + label)
        time.sleep(0.06)
    raise AssertionError('Timed out during ' + label)


def command(peer, action, **values):
    sequences[peer] += 1
    path = report / (peer + '.command.json')
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(dict(sequence=sequences[peer], action=action, **values)))
    temporary.replace(path)
    wait(lambda: read(peer).get('lastCommand') == sequences[peer], action)


def traffic(peer, action, scenario=None, **values):
    traffic_sequences[peer] += 1
    path = report / (peer + '.traffic-command.json')
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(dict(sequence=traffic_sequences[peer], action=action, **values)))
    temporary.replace(path)
    wait(lambda: read(peer, '.traffic').get('sequence') == traffic_sequences[peer], action, 30)
    value = read(peer, '.traffic')
    if not value['success']:
        raise AssertionError(value.get('error', action))
    if scenario:
        value.update(peer=peer, scenario=scenario)
        measurements.append(value)
        print(json.dumps(value), flush=True)
    return value


try:
    for peer in ['host', 'c1', 'c2', 'c3']:
        sequences[peer] = 0
        traffic_sequences[peer] = 0
        for suffix in ['.json', '.command.json', '.traffic.json', '.traffic-command.json']:
            (report / (peer + suffix)).unlink(missing_ok=True)
        parameters = [str(args.player.resolve()), '-batchmode', '-nographics', '-networkTrafficBenchmark',
                      '-lobbyProbeDirectory', str(report), '-lobbyProbeId', peer,
                      '-logFile', str(report / (peer + '-player.log'))]
        if peer == 'host':
            parameters += ['-lobbyProbeManual']
        logs[peer] = (report / (peer + '.log')).open('w')
        processes[peer] = subprocess.Popen(parameters, stdout=logs[peer], stderr=subprocess.STDOUT)
        wait(lambda: bool(read(peer)), peer + ' launch')
        if peer == 'host':
            command(peer, 'host')
        wait(lambda: read(peer).get('screen') == 'Room', peer + ' room')
    wait(lambda: all(len(read(peer).get('players', [])) == 4 for peer in processes), 'four player roster')
    for peer in processes:
        command(peer, 'ready', ready=True)
    wait(lambda: all(read(peer).get('racePhase') == 'Racing' for peer in processes), 'game starts', 55)
    time.sleep(0.8)
    defender = next(peer for peer in ['c1', 'c2', 'c3']
                    if next(player['id'] for player in read(peer)['gamePlayers'] if player['local']) != read('host')['attacker'])
    owner = next(player['id'] for player in read(defender)['gamePlayers'] if player['local'])
    result['defender'] = defender
    result['ownerId'] = owner
    for action in ['aimStationary', 'aimMoving']:
        traffic('host', 'prepareAim', ownerId=owner)
        time.sleep(0.3)  # Deliver the owner-only active-missile SyncVar before the measurement.
        value = traffic(defender, action, scenario=action, duration=4)
        assert value['messages'] > 0, value
        assert value['reliableMessages'] == 0 and value['unreliableMessages'] == value['messages'], value
        traffic('host', 'cleanup')
        time.sleep(0.15)
    traffic('host', 'prepareRound', ownerId=owner)
    stationary = traffic('host', 'roundStationary', scenario='roundStationary', duration=4)
    assert stationary['unreliableMessages'] > 0 and stationary['reliableMessages'] == 0, stationary
    expiry = traffic('host', 'roundStationary', scenario='roundExpiry', duration=4)
    assert expiry['expiredMessages'] == 1 and expiry['reliableMessages'] == 1, expiry
    traffic('host', 'cleanup')
    traffic('host', 'prepareMovingRound', ownerId=owner)
    moving = traffic('host', 'roundMoving', scenario='roundMoving', duration=1)
    assert moving['unreliableMessages'] > 0 and moving['expiredMessages'] == 0, moving
    traffic('host', 'cleanup')
    assert all(not read(peer)['errors'] for peer in processes), {peer: read(peer)['errors'] for peer in processes}
    if args.baseline:
        baseline = json.loads(args.baseline.read_text())
        assert baseline['success'], baseline.get('error')
        previous = {measurement['scenario']: measurement for measurement in baseline['measurements']}
        current = {measurement['scenario']: measurement for measurement in measurements}
        comparisons = {}
        for name, value in current.items():
            before_rate = previous[name]['messages'] / previous[name]['elapsed']
            after_rate = value['messages'] / value['elapsed']
            comparisons[name] = dict(beforeRate=before_rate, afterRate=after_rate,
                                     reductionPercent=100 * (1 - after_rate / before_rate))
        assert comparisons['aimStationary']['reductionPercent'] > 60, comparisons
        assert comparisons['roundStationary']['reductionPercent'] > 60, comparisons
        for name in ['aimMoving', 'roundMoving']:
            ratio = comparisons[name]['afterRate'] / comparisons[name]['beforeRate']
            assert 0.75 <= ratio <= 1.25, (name, ratio, comparisons)
        result['comparisons'] = comparisons
    if args.baseline:
        traffic('host', 'prepareAim', ownerId=owner)
        time.sleep(0.3)
        recovery = {}

        def restore_aim():
            try:
                time.sleep(0.6)
                damaged = traffic('host', 'corruptAim')
                assert damaged['aimX'] == -10000, damaged
                started = time.monotonic()
                while time.monotonic() - started < 1:
                    observed = traffic('host', 'inspectAim')
                    if observed['aimX'] != -10000:
                        recovery['seconds'] = time.monotonic() - started
                        return
                recovery['error'] = 'Heartbeat did not restore the server aim'
            except Exception as error:
                recovery['error'] = str(error)

        worker = threading.Thread(target=restore_aim)
        worker.start()
        value = traffic(defender, 'aimStationary', duration=2)
        worker.join(3)
        assert 'error' not in recovery and recovery.get('seconds', 2) < 0.5, recovery
        assert value['firstMessageDelay'] < 0.1 and value['largestMessageGap'] < 0.35, value
        result['stationaryAimRecovery'] = recovery
        traffic('host', 'cleanup')
    result['success'] = True
    print('TRAFFIC_BENCHMARK_PASSED', flush=True)
except Exception as error:
    result['error'] = str(error)
    print('FAILED ' + str(error), flush=True)
finally:
    result['measurements'] = measurements
    result['snapshots'] = {peer: read(peer) for peer in processes}
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
