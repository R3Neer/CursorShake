import pathlib,json,csv,hashlib,argparse
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

def reconstruct(raw,axis,threshold):
 xy=np.array([[float(s['x_physical_px']),float(s['y_physical_px'])] for s in raw]);times=np.array([float(s['received_ms']) for s in raw]);proj=(xy-xy.mean(axis=0))@axis
 start=ext=leg=proj[0];idx=0;direction=0;turns=[]
 for i,v in enumerate(proj[1:],1):
  if direction==0:
   if abs(v-start)>=threshold:direction=1 if v>start else -1;ext=v;idx=i
   continue
  if (v-ext)*direction>=0:ext=v;idx=i;continue
  if (ext-v)*direction<threshold:continue
  turns.append({'ms':float(times[idx]),'excursionPx':float(abs(ext-leg))});leg=ext;direction=-direction;ext=v;idx=i
 for i in range(1,len(turns)):
  dt=turns[i]['ms']-turns[i-1]['ms'];turns[i]['intervalMs']=dt;turns[i]['speedPxPerSecond']=turns[i]['excursionPx']*1000/dt if dt>0 else None
 return [t for t in turns if t.get('intervalMs',0)>0]

def metrics(turns):
 v=np.array([t['intervalMs'] for t in turns]);passes={}
 for limit in [100,120,125,130,135,140,150]:
  flags=v<=limit;passes[str(limit)]={'acceptedIntervals':int(sum(flags)),'totalIntervals':len(v),'percent':float(np.mean(flags)*100),'acceptedThreeTurnWindows':int(sum(flags[:-1]&flags[1:]))}
 return {'count':len(v),'quantilesMs':dict(zip(['min','q25','median','q75','p90','p95','max'],np.percentile(v,[0,25,50,75,90,95,100]).tolist())),'sub10msCount':int(sum(v<10)),'thresholds':passes}

def main():
 ap=argparse.ArgumentParser();ap.add_argument('sessions');ap.add_argument('output');args=ap.parse_args();src=pathlib.Path(args.sessions);out=pathlib.Path(args.output);out.mkdir(parents=True,exist_ok=True)
 groups={}
 for p in sorted(src.glob('*/result.json')):
  d=json.loads(p.read_text(encoding='utf-8-sig'))
  if d.get('GuidedRunId') and d.get('Reason')=='completed':groups.setdefault(d['GuidedRunId'],{})[d['Stage']]=(p,d)
 order=['medium-1','slow','fast','medium-2'];complete=[(k,v) for k,v in groups.items() if all(s in v for s in order)];assert complete,'No complete guided trial';gid,group=complete[-1]
 report={'group':gid,'proposedMaximumIntervalMs':130,'adoptedAsDefault':False,'intervalClock':'Stopwatch at hook reception','reanalysisThresholdsPhysicalPx':[8,16,32,48,64],'warning':'Exploratory calibration of one user; 32px is an analysis sensitivity check, not an approved recognition default. No time-based interval exclusions.','stages':[]};plotdata=[];speeddata=[]
 for stage in order:
  p,d=group[stage];raw=list(csv.DictReader((p.parent/'movement.csv').open(encoding='utf-8-sig')));axis=np.array([d['Final']['AxisX'],d['Final']['AxisY']]);analyses={}
  for threshold in [8,16,32,48,64]:
   turns=reconstruct(raw,axis,threshold);analyses[str(threshold)]=metrics(turns)
   if threshold==32:
    plotdata.append([t['intervalMs'] for t in turns]);speeddata.append([t['speedPxPerSecond'] for t in turns])
  assert analyses['8']['count']==len(d['Final']['Turns'])-1
  report['stages'].append({'stage':stage,'session':p.parent.name,'sourceSha256':hashlib.sha256((p.parent/'movement.csv').read_bytes()).hexdigest(),'analysisBySpatialThresholdPx':analyses})
 labels=['Medio 1','Lento','Rápido','Medio 2'];colors=['#306b9a','#b86b36','#32816b','#306b9a']
 plt.rcParams.update({'font.family':'DejaVu Sans','font.size':11,'axes.spines.top':False,'axes.spines.right':False})
 fig,axes=plt.subplots(1,2,figsize=(11.5,5.7),layout='constrained')
 for ax,data in zip(axes,[plotdata,speeddata]):
  boxes=ax.boxplot(data,tick_labels=labels,patch_artist=True,widths=.5,showfliers=False,medianprops={'color':'#222','linewidth':2})
  for box,color in zip(boxes['boxes'],colors):box.set_facecolor(color);box.set_alpha(.25)
  rng=np.random.default_rng(42)
  for i,(v,color) in enumerate(zip(data,colors),1):ax.scatter(i+rng.uniform(-.12,.12,len(v)),v,s=13,alpha=.5,color=color,zorder=2)
  ax.grid(axis='y',alpha=.18);ax.set_axisbelow(True)
 axes[0].axhline(130,color='#a03232',ls='--',lw=1.5,label='Propuesta: 130 ms');axes[0].legend(frameon=False);axes[0].set(ylabel='Intervalo entre inversiones (ms)',title='Cadencia: menor intervalo = agitación más rápida')
 axes[1].set(ylabel='Velocidad media proyectada (px físicos/s)',title='Velocidad del desplazamiento')
 fig.suptitle('CursorShake · cuatro tramos etiquetados de seis segundos',fontsize=15)
 fig.supxlabel('Reanálisis uniforme con filtro espacial de 32 px; todos los intervalos, incluidas pausas.\nCajas: cuartiles y mediana · bigotes: 1,5 IQR · puntos: cada intervalo registrado.',fontsize=10)
 fig.savefig(out/'ritmos-etiquetados.png',dpi=170);fig.savefig(out/'ritmos-etiquetados.svg');plt.close(fig)
 (out/'guided-analysis.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps({s['stage']:s['analysisBySpatialThresholdPx']['32'] for s in report['stages']},indent=2))
if __name__=='__main__':main()
