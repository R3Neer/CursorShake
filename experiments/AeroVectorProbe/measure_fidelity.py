"""Measure the contact sheet generated for the Aero vector feasibility probe."""
import argparse,pathlib,json
import numpy as np
from PIL import Image
ap=argparse.ArgumentParser();ap.add_argument('capture');ap.add_argument('output');args=ap.parse_args()
im=np.array(Image.open(args.capture).convert('RGB'));assert im.shape[0]>=768 and im.shape[1]>=384
rows=[]
for j in range(36):
 y=j//3*64;x=j%3*128
 a=im[y:y+64,x:x+64].astype(float);b=im[y:y+64,x+64:x+128].astype(float)
 support=(a.min(axis=2)<250)|(b.min(axis=2)<250);diff=np.abs(a-b)
 rows.append({'role':'wait' if j<18 else 'working','frame':j%18,'meanAbsChannel':float(diff.mean()),'rmseChannel':float(np.sqrt((diff**2).mean())),'supportMeanAbsChannel':float(diff[support].mean())})
result={'description':'Chromium rasterization at 64 px compared with source ANI on white; approximate reconstruction, not identity proof','comparisons':rows,'meanAbsChannelByRole':{role:sum(r['meanAbsChannel'] for r in rows if r['role']==role)/18 for role in ['wait','working']}}
pathlib.Path(args.output).write_text(json.dumps(result,indent=2),encoding='utf-8')
print(result['meanAbsChannelByRole'])
