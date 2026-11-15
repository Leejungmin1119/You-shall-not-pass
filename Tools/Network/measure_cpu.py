#!/usr/bin/env python3
"""Compare opt-in code-section benchmarks with four real KCP players."""
import argparse
import json
from pathlib import Path
import statistics
import subprocess
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--player', type=Path, required=True)
parser.add_argument('--report-dir', type=Path, required=True)
parser.add_argument('--baseline', type=Path)
args = parser.parse_args()
report = args.report_dir.resolve()
report.mkdir(parents=True, exist_ok=True)
processes = {}
logs = {}
sequences = {}
performance_sequences = {}
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
        time.sleep(0.08)
    raise AssertionError('Timed out during ' + label)


def command(peer, action, **values):
    sequences[peer] += 1
    path = report / (peer + '.command.json')
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(dict(sequence=sequences[peer], action=action, **values)))
    temporary.replace(path)
    wait(lambda: read(peer).get('lastCommand') == sequences[peer], action)


def measure(peer, action, **values):
    performance_sequences[peer] += 1
    path = report / (peer + '.perf-command.json')
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(dict(sequence=performance_sequences[peer], action=action,
                                         iterations=10000, **values)))
    temporary.replace(path)
    wait(lambda: read(peer, '.perf').get('sequence') == performance_sequences[peer], action)
    value = read(peer, '.perf')
    assert value['success'], value.get('error')
    assert value['players'] == 4, value
    assert value['hits'] == (40000 if action == 'roster' else 10000), value
    measurements.append(dict(peer=peer, **value))
    print(json.dumps(measurements[-1]), flush=True)


def summarize(samples):
    groups = {}
    for sample in samples:
        key = (sample['action'], sample['peer'], sample['extraObjects'], sample['iterations'])
        groups.setdefault(key, []).append(sample['milliseconds'])
    return {key: statistics.median(times) for key, times in groups.items()}


try:
    for peer in ['host', 'c1', 'c2', 'c3']:
        sequences[peer] = 0
        performance_sequences[peer] = 0
        for suffix in ['.json', '.command.json', '.perf.json', '.perf-command.json']:
            (report / (peer + suffix)).unlink(missing_ok=True)
        parameters = [str(args.player.resolve()), '-batchmode', '-nographics', '-networkCpuBenchmark',
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
    wait(lambda: all(len(read(peer)['players']) == 4 for peer in processes), 'roster')
    for peer in ['host', 'c1']:
        for trial in range(5):
            measure(peer, 'roster')
    for peer in processes:
        command(peer, 'ready', ready=True)
    wait(lambda: all(read(peer).get('racePhase') == 'Racing' for peer in processes), 'game starts', 55)
    time.sleep(0.7)
    for extra in [0, 64, 256]:
        for trial in range(5):
            measure('host', 'collision', extraObjects=extra)
    assert all(not read(peer)['errors'] for peer in processes), {peer: read(peer)['errors'] for peer in processes}
    assert all(read(peer).get('serverRegistryValid', True) for peer in processes)
    result['allocationCounterAvailable'] = all(sample.get('allocationCounterAvailable', False) for sample in measurements)
    if args.baseline:
        baseline = json.loads(args.baseline.read_text())
        assert baseline['success'], baseline.get('error')
        before = summarize(baseline['measurements'])
        after = summarize(measurements)
        comparisons = []
        for key, elapsed in after.items():
            previous = before[key]
            comparisons.append(dict(action=key[0], peer=key[1], extraObjects=key[2], iterations=key[3],
                                    beforeMilliseconds=previous, afterMilliseconds=elapsed,
                                    reductionPercent=100 * (1 - elapsed / previous)))
            assert elapsed < previous * 0.8, comparisons[-1]
        result['comparisons'] = comparisons
    result['success'] = True
    print('CPU_BENCHMARK_PASSED', flush=True)
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
