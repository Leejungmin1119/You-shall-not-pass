#!/usr/bin/env python3
"""Development Player integration checks. Goal placement is an explicit server fixture."""
from pathlib import Path
import subprocess,time,json,sys
import argparse
parser=argparse.ArgumentParser(description='Actual KCP host/client lobby and game smoke checks. Development Player only.')
parser.add_argument('--player',type=Path,required=True,help='macOS .app/Contents/MacOS executable')
parser.add_argument('--report-dir',type=Path,required=True)
args=parser.parse_args()
REPORT=args.report_dir.resolve();REPORT.mkdir(parents=True,exist_ok=True)
EXE=args.player.resolve()
procs={};logs={};seq={};passed=[];details={}
def check(condition,label):
 if not condition:raise AssertionError(label)
 passed.append(label);print('PASS '+label,flush=True)
def read(ident):
 try:return json.loads((REPORT/(ident+'.json')).read_text())
 except (OSError,ValueError):return {}
def wait(predicate,label,timeout=20):
 deadline=time.monotonic()+timeout
 while time.monotonic()<deadline:
  if predicate():return
  time.sleep(.12)
 raise AssertionError('TIMEOUT: '+label+'; snapshots '+json.dumps({i:read(i) for i in procs},ensure_ascii=False)[:7000])
def start(ident):
 for suffix in ['.json','.command.json','.events.jsonl']:(REPORT/(ident+suffix)).unlink(missing_ok=True)
 seq[ident]=0
 executable=EXE
 args=[str(executable),'-batchmode','-nographics','-lobbyProbeDirectory',str(REPORT),'-lobbyProbeId',ident,'-logFile',str(REPORT/(ident+'-player.log'))]
 if ident=='host':args+=['-lobbyProbeManual']
 logs[ident]=(REPORT/(ident+'.log')).open('w')
 procs[ident]=subprocess.Popen(args,stdout=logs[ident],stderr=subprocess.STDOUT)
 if ident=='host':
  wait(lambda:read(ident).get('screen')=='Main','initial main menu',45)
  check(not read(ident)['connected'] and not read(ident)['advertising'],'offline opens main menu without room or ready controls')
  command(ident,'host')
 wait(lambda:read(ident).get('connected') and read(ident).get('localPlayerId',0)>0,ident+' connected',45)
 wait(lambda:read(ident).get('screen')=='Room',ident+' waiting room view')
 check(True,'connected player enters waiting room '+ident)
 print('CONNECTED '+ident,flush=True)
def command(ident,action,**kwargs):
 seq[ident]+=1;c={'sequence':seq[ident],'action':action,**kwargs}
 p=REPORT/(ident+'.command.json');t=p.with_suffix('.tmp');t.write_text(json.dumps(c));t.replace(p)
 wait(lambda:read(ident).get('lastCommand',0)==seq[ident],ident+' '+action)
def nplayers(n):return len(read('host').get('players',[]))==n
def settle(n,ids):wait(lambda:nplayers(n) and all(len(read(i).get('players',[]))==n for i in ids),'roster '+str(n))
def countdown(ids):
 wait(lambda:all(read(i).get('countdown') and read(i).get('deadline',0)>read(i).get('now',0) for i in ids),'replicated countdown')
 deadline=read('host')['deadline']
 check(all(abs(read(i)['deadline']-deadline)<.001 for i in ids),'identical countdown deadline '+','.join(ids))
 return deadline
def no_countdown(ids):wait(lambda:all(read(i).get('deadline')==0 and not read(i).get('countdown') for i in ids),'countdown cancelled')
def ready(ident,value=True):command(ident,'ready',ready=value)
def game(ids,deadline):
 wait(lambda:all(read(i).get('scene')=='Stage1' for i in ids),'game scene for '+','.join(ids),35)
 events={i:[json.loads(line) for line in (REPORT/(i+'.events.jsonl')).read_text().splitlines()] for i in ids}
 entries={i:max(e['now'] for e in events[i] if e['type']=='scene' and e['scene']=='Stage1') for i in ids}
 check(all(t>=deadline-.08 for t in entries.values()),'game starts after full server deadline '+str(len(ids))+' players')
 check(all(read(i)['screen']=='Game' for i in ids),'gameplay uses game view on every peer')
def returned(ids):
 command('host','return');wait(lambda:all(read(i).get('scene')=='LobbyScene' for i in ids),'return to lobby',30);settle(len(ids),ids)
 wait(lambda:all(not p['ready'] for p in read('host')['players']),'readiness reset')
 no_countdown(ids);check(True,'return to lobby clears ready and countdown')
 check(all(read(i)['screen']=='Room' for i in ids),'return restores waiting room, not main menu')
 wait(lambda:read('host').get('advertising'),'LAN advertisement restored');check(True,'LAN advertisement restored after game')
def stop(ident):
 process=procs.pop(ident,None)
 if process is not None:
  process.terminate()
  try:process.wait(5)
  except subprocess.TimeoutExpired:process.kill();process.wait()
  logs[ident].close()
try:
 start('host');settle(1,['host']);ready('host');time.sleep(.6)
 check(read('host')['deadline']==0,'one ready player cannot auto-start')
 start('c1');settle(2,['host','c1'])
 wait(lambda:all(p['seat']>=0 for p in read('c1')['players']),'seat replication')
 check([p['seat'] for p in read('host')['players']]==[0,1],'first two seats assigned uniquely')
 command('c1','nudge');time.sleep(.6)
 for ident in ['host','c1']:
  check(all(abs(p['x']-(p['seat']-1.5)*2.8)<.01 and abs(p['y'])<.01 for p in read(ident)['players']),'fixed positions after nudge on '+ident)
 ready('c1');first=countdown(['host','c1'])
 command('host','tryStart');check(read('host')['scene']=='LobbyScene','manual start cannot bypass countdown')
 ready('c1',False);no_countdown(['host','c1']);time.sleep(5.3)
 check(all(read(i)['scene']=='LobbyScene' for i in ['host','c1']),'cancelled deadline cannot start game later')
 ready('c1');second=countdown(['host','c1']);check(second>first+5,'re-ready starts a fresh five second deadline')
 command('c1','rename',name='ReadyTester');no_countdown(['host','c1'])
 wait(lambda:any(p['name']=='ReadyTester' and not p['ready'] for p in read('host')['players']),'name change propagated')
 check(True,'name edit cancels readiness and countdown')
 ready('c1');deadline=countdown(['host','c1']);game(['host','c1'],deadline)
 returned(['host','c1'])
 ready('host');ready('c1');countdown(['host','c1'])
 start('c2');settle(3,['host','c1','c2']);no_countdown(['host','c1','c2'])
 check(True,'join during countdown cancels start on all peers')
 ready('c2');countdown(['host','c1','c2'])
 command('c2','leave');settle(2,['host','c1']);no_countdown(['host','c1'])
 check(True,'departure cancels countdown')
 check([p['seat'] for p in read('host')['players']]==[0,1],'departure leaves other seats unchanged')
 stop('c2');start('c3');settle(3,['host','c1','c3']);start('c2');ids=['host','c1','c2','c3'];settle(4,ids)
 check(sorted(p['seat'] for p in read('host')['players'])==[0,1,2,3],'four players occupy all four unique seats')
 roster={p['netId']:p['seat'] for p in read('host')['players']}
 for i in ids:ready(i)
 deadline=countdown(ids);details['fourPlayerDeadline']=deadline;game(ids,deadline)
 returned(ids)
 before={p['netId']:p['seat'] for p in read('host')['players']};leaving=next(p for p in read('c1')['players'] if p['local'])
 command('c1','leave');remain=['host','c2','c3'];settle(3,remain);stop('c1')
 check(all(before[p['netId']]==p['seat'] for p in read('host')['players']),'middle seat departure does not shift remaining players')
 start('c1');settle(4,ids)
 check(next(p for p in read('c1')['players'] if p['local'])['seat']==leaving['seat'],'rejoining player fills vacant middle seat')
 for i in ids:check(not read(i)['errors'],'no runtime errors on '+i)

 # Latest Stage1, actual client Commands and server goal-contact fixture.
 for i in ids: ready(i)
 deadline=countdown(ids);game(ids,deadline)
 wait(lambda:all(read(i).get('racePhase')=='Racing' for i in ids),'four player racing',35)
 check(not read('host')['advertising'],'game hides room advertisement')
 attacker=read('host')['attacker'];turn=read('host')['turn']
 defender=next(i for i in ['c1','c2','c3'] if next(p['id'] for p in read(i)['gamePlayers'] if p['local'])!=attacker)
 owner=next(p['id'] for p in read(defender)['gamePlayers'] if p['local'])
 command(defender,'buy')
 wait(lambda:'purchase:True' in read(defender)['requestResults'],'remote purchase response')
 wait(lambda:all(next(p for p in read(i)['gamePlayers'] if p['id']==owner)['count']==1 for i in ids),'public inventory count')
 check(True,'remote purchase and public count replicated')
 for i in ['c1','c2','c3']:
  check(all(len(p['slots'])==0 for p in read(i)['gamePlayers'] if not p['local']),'owner-only inventory on '+i)
 command(defender,'staleUse')
 wait(lambda:'use:False' in read(defender)['requestResults'],'stale invalid use rejected')
 check(next(p for p in read(defender)['gamePlayers'] if p['local'])['count']==1,'rejected use preserves inventory')
 command(defender,'use')
 wait(lambda:'use:True' in read(defender)['requestResults'],'valid use response')
 wait(lambda:all(read(i)['maxTraps']>0 for i in ids),'trap appears on all peers')
 check(True,'actual remote use and trap spawn replicated to four peers')
 command('host','goalFixture')
 wait(lambda:all(read(i)['turn']==turn+1 and read(i)['attacker']!=attacker for i in ids),'goal turn propagation')
 wait(lambda:all(next(p for p in read(i)['gamePlayers'] if p['id']==attacker)['score']==1 for i in ids),'goal score propagation')
 check(len(read('host')['serverChecks'])==3,'server rejects stale/remote goal then accepts contact fixture')
 check(True,'goal score and attacker turn identical on four peers')
 wait(lambda:all(all(p['count']==0 for p in read(i)['gamePlayers']) for i in ids),'turn resets inventories')
 check(True,'turn reset clears inventory on every peer')
 command('c3','leave');remain=['host','c1','c2']
 wait(lambda:all(read(i)['scene']=='LobbyScene' for i in remain),'disconnect abort returns all remaining peers',30)
 wait(lambda:read('host')['advertising'],'advertise after disconnected game')
 check(not read('c3')['connectionError'],'voluntary leave has no failure notice')
 check(True,'game departure aborts match and restores searchable lobby')
 # Exercise discovery rather than the direct loopback address used by the baseline checks.
 command('c3','search')
 wait(lambda:read('c3')['connected'] and len(read('c3')['players'])==4,'real UDP discovery rejoin',25)
 check(True,'UDP discovery finds returned lobby and reconnects')
 command('host','leave')
 wait(lambda:all(not read(i)['connected'] and read(i)['scene']=='LobbyScene' for i in ['c1','c2','c3']),'host loss cleanup',25)
 check(all(read(i)['connectionError'] for i in ['c1','c2','c3']),'host loss shown to each remote client')
 check(all(read(i)['screen']=='Main' for i in ids),'host loss and voluntary leave restore main menu')
 check(not read('host')['connectionError'],'voluntary host leave is not an error')
 command('c1','direct',name='not-an-ip')
 wait(lambda:read('c1')['connectionError'] and not read('c1')['connected'],'invalid direct address rejected')
 check(True,'invalid IPv4 produces notice without starting client')
 started=time.monotonic();command('c2','direct',name='127.0.0.1')
 wait(lambda:read('c2')['connectionError'] and not read('c2')['connected'],'closed host bounded timeout',16)
 check(time.monotonic()-started<15,'unreachable host stops waiting within 15 seconds')
 command('host','host');wait(lambda:read('host')['advertising'] and read('host')['connected'],'host restart')
 command('c1','direct',name='127.0.0.1')
 wait(lambda:read('c1')['connected'] and len(read('c1')['players'])==2,'direct retry after invalid address')
 check(not read('c1')['connectionError'],'successful direct retry clears old failure')
 command('c2','direct',name='127.0.0.1')
 wait(lambda:read('c2')['connected'] and len(read('c2')['players'])==3,'direct retry after timeout')
 check(not read('c2')['connectionError'],'client recovers after timeout without restart')
 for i in ids:check(not read(i)['errors'],'no runtime errors after recovery on '+i)
 command('c3','direct',name='127.0.0.1');settle(4,ids)
 for i in ids:ready(i)
 deadline=countdown(ids);game(ids,deadline)
 command('c3','leave')
 wait(lambda:all(read(i)['scene']=='LobbyScene' for i in ['host','c1','c2']),'pre-start departure returns lobby',30)
 wait(lambda:read('host')['advertising'],'pre-start departure restores advertising')
 check(True,'departure during game preparation aborts and restores LAN lobby')
 command('c3','solo')
 wait(lambda:read('c3')['scene']=='Stage1' and read('c3')['screen']=='Game','solo test launches directly',30)
 check(not read('c3')['advertising'],'solo test bypasses waiting room and does not advertise')
 events=[json.loads(line) for line in (REPORT/'c3.events.jsonl').read_text().splitlines()]
 solo_index=max(i for i,e in enumerate(events) if e['type']=='command-solo')
 check(all(e.get('screen')!='Room' for e in events[solo_index:]),'solo transition never exposes waiting room')
 command('c3','return')
 wait(lambda:read('c3')['screen']=='Main' and not read('c3')['connected'],'solo test returns to main menu',30)
 check(True,'solo exit returns to offline main menu')
 print('ALL '+str(len(passed))+' CHECKS PASSED',flush=True)
 details['success']=True
except Exception as exc:
 details['success']=False;details['error']=str(exc);print('FAILED '+str(exc),flush=True)
finally:
 details['passed']=passed;details['snapshots']={i:read(i) for i in procs}
 (REPORT/'result.json').write_text(json.dumps(details,ensure_ascii=False,indent=2))
 for i in list(procs):stop(i)
if not details['success']:sys.exit(1)
