"""Isolated visual feasibility probe. No cursor replacement or product implementation.
Python/numpy are used for image measurement; proposed product remains C#.
Original Windows resources are read locally and never copied into tracked output.
"""
import pathlib,struct,base64,zlib,json,hashlib,math,io,argparse
import numpy as np
from PIL import Image

def chunks(d):
 p=0
 while p+8<=len(d):
  tag,n=struct.unpack_from('<4sI',d,p);x=d[p+8:p+8+n];p+=8+n+(n&1)
  if tag==b'LIST':yield from chunks(x[4:])
  else:yield tag,x

def image_from_cur(cur):
 entries=[struct.unpack_from('<BBBBHHII',cur,6+i*16) for i in range(struct.unpack_from('<H',cur,4)[0])]
 e=max(entries,key=lambda e:e[0] or 256);w=e[0] or 256;h=e[1] or 256;d=cur[e[-1]:e[-1]+e[-2]]
 hs,bw,bh,planes,bpp,comp=struct.unpack_from('<IiiHHI',d);assert hs==40 and bpp==32 and comp==0
 raw=np.frombuffer(d[hs:hs+w*h*4],dtype=np.uint8).reshape(h,w,4)[::-1].copy();raw=raw[:,:,[2,1,0,3]]
 return Image.fromarray(raw),[e[4],e[5]]

def fit(im,cx,cy,inner,outer):
 a=np.array(im);yy,xx=np.indices(a.shape[:2]);dx=xx+.5-cx;dy=yy+.5-cy;r=np.hypot(dx,dy)
 sel=(r>inner+1.5)&(r<outer-1.5)&(a[:,:,3]==255)&(a[:,:,2]>200)
 angle=np.degrees(np.arctan2(dy[sel],dx[sel]));level=np.clip((a[:,:,1][sel].astype(float)-151)/53,0,1)
 center=math.degrees(math.atan2(np.sum(np.sin(np.radians(angle))*level),np.sum(np.cos(np.radians(angle))*level)))%360
 best=(1e9,None)
 for c in np.arange(center-2,center+2.1,.25):
  delta=np.abs((angle-c+180)%360-180)
  for hw in np.arange(30,47,1):
   for fade in np.arange(6,23,1):
    v=np.clip((hw-delta)/fade,0,1);err=float(np.mean((level-v)**2))
    if err<best[0]:best=(err,{'centerDegrees':round(float(c%360),3),'halfWidthDegrees':float(hw),'fadeDegrees':float(fade),'interiorColorRmse':round(math.sqrt(err)*53,4)})
 return best[1]

def svg_frame(geometry,fit,working):
 cx,cy,inner,outer=geometry;radius=(inner+outer)/2;stroke=outer-inner
 def point(angle):a=math.radians(angle);return cx+radius*math.cos(a),cy+radius*math.sin(a)
 parts=['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">', '<defs><filter id="s" x="-25%" y="-25%" width="150%" height="150%"><feGaussianBlur stdDeviation="0.4"/></filter></defs>',f'<circle cx="{cx}" cy="{cy}" r="{radius}" fill="none" stroke="#000" stroke-opacity=".16" stroke-width="{stroke+1}" filter="url(#s)"/>',f'<circle cx="{cx}" cy="{cy}" r="{radius}" fill="none" stroke="#2a97f3" stroke-width="{stroke}"/>']
 center=fit['centerDegrees'];hw=fit['halfWidthDegrees'];fade=fit['fadeDegrees']
 # Analytic annular sectors, no embedded pixels. Fine angular subdivisions only
 # approximate the color ramp; the circular silhouette remains vector geometry.
 for k in range(-math.ceil(hw),math.ceil(hw),2):
  angle=center+k;amount=max(0,min(1,(hw-abs(k+1))/fade))
  if amount<=0:continue
  color=tuple(round(a+(b-a)*amount) for a,b in zip((42,151,243),(37,204,255)))
  a=math.radians(angle-.65);b=math.radians(angle+2.65)
  def pt(r,t):return f'{cx+r*math.cos(t):.4f},{cy+r*math.sin(t):.4f}'
  path=f'M{pt(outer,a)} A{outer},{outer} 0 0 1 {pt(outer,b)} L{pt(inner,b)} A{inner},{inner} 0 0 0 {pt(inner,a)} Z'
  parts.append(f'<path fill="rgb{color}" d="{path}"/>'.replace('rgb(', 'rgb('))
 if working:
  parts.append('<path d="M0.5 16.5 L0.5 49 L7.7 42.5 L12.9 53.2 L17.4 51.6 L13 40.5 L24.5 40.5 Z" fill="white" stroke="#000" stroke-width="1" stroke-linejoin="round"/>')
 parts.append('</svg>');return ''.join(parts)

def uri(data,mime):return 'data:'+mime+';base64,'+base64.b64encode(data).decode()

def main():
 ap=argparse.ArgumentParser();ap.add_argument('output');ap.add_argument('template');args=ap.parse_args();root=pathlib.Path(args.output);root.mkdir(parents=True,exist_ok=False);payload={};report=[]
 for key,name,geometry in [('wait','aero_busy.ani',(32,32,11.7,19.8)),('working','aero_working.ani',(28.5,16.5,8.75,15.05))]:
  path=pathlib.Path('C:/Windows/Cursors')/name;raw=path.read_bytes();cs=list(chunks(raw[12:]));header=struct.unpack('<9I',next(x for t,x in cs if t==b'anih'));frames=[image_from_cur(x) for t,x in cs if t==b'icon'];steps=header[2];seq=next((list(struct.unpack('<'+'I'*(len(x)//4),x)) for t,x in cs if t==b'seq '),list(range(steps)));rates=next((list(struct.unpack('<'+'I'*(len(x)//4),x)) for t,x in cs if t==b'rate'),[header[7]]*steps)
  originals=[];vectors=[];fits=[]
  for i,(im,hotspot) in enumerate(frames):
   f=fit(im,*geometry);fits.append(f);svg=svg_frame(geometry,f,key=='working');(root/f'{key}-{i:02}.svg').write_text(svg,encoding='utf-8');buf=io.BytesIO();im.save(buf,format='PNG');originals.append(uri(buf.getvalue(),'image/png'));vectors.append(uri(svg.encode(),'image/svg+xml'))
  payload[key]={'frames':originals,'vectors':vectors,'durations':[r*1000/60 for r in rates],'sequence':seq}
  report.append({'role':key,'sourceName':name,'sourceSha256':hashlib.sha256(raw).hexdigest(),'nativeSize':list(frames[0][0].size),'hotspot':frames[0][1],'frames':len(frames),'stepDurationsMs':payload[key]['durations'],'geometry':dict(zip(['centerX','centerY','innerRadius','outerRadius'],geometry)),'fits':fits,'approximation':True})
 template=pathlib.Path(args.template).read_text(encoding='utf-8');html=template.replace('__CURSOR_DATA__',json.dumps(payload,separators=(',',':')));assert len(html.encode())<1000000
 (root/'aero-vector-comparison.html').write_text(html,encoding='utf-8');(root/'measurements.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print('Created',root,'bytes',len(html.encode()))
if __name__=='__main__':main()
