"""One-time source preparation; normal builds only pack the checked-in editable SVGs.
Python reuses the XML/source-processing approach of the existing vector experiments.
No system cursor is changed and no Windows images are redistributed.
"""
from pathlib import Path
import urllib.request, zipfile, posixpath, tomllib, json, hashlib, re
import xml.etree.ElementTree as E

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'assets/themes'
BIBATA='ddb09273cde49faeb6a32282660ff4293f52d6e0'
NORDZY='c7fc485e9e4fd974c4f4ff9f5f14610fa7835e7b'
def write(folder,name,text):
    p=folder/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text,encoding='utf-8',newline='\n')
def resource(folder,role,images,hots,delays=None):
    d=folder/role;text='version = 1\n'
    if delays is None:
        write(d,'frame.svg',images[0]);text+=f'image = "frame.svg"\nhotspot = [{hots[0][0]}, {hots[0][1]}]\n'
    else:
        for i,(source,hot,delay) in enumerate(zip(images,hots,delays)):
            name=f'frame-{i:03}.svg';write(d,name,source)
            text+=f'\n[[frames]]\nimage = "{name}"\nduration_ms = {delay!r}\nhotspot = [{hot[0]}, {hot[1]}]\n'
    write(d,'cursor.toml',text)
def manifest(folder,roles,title,default=True):
    text=f'id = "{folder.name}"\nname = "{title}"\n'
    if default:text+='default = "arrow.vcur"\n'
    text+='\n[roles]\n'+''.join(f'{role} = "{role}{ext}"\n' for role,ext in roles.items())
    write(folder,'theme.toml',text)
def bibata():
    archive=ROOT/'artifacts/vendor/bibata-source.zip';archive.parent.mkdir(parents=True,exist_ok=True)
    if not archive.exists():
        with urllib.request.urlopen(f'https://codeload.github.com/ful1e5/Bibata_Cursor/zip/{BIBATA}',timeout=90) as response:archive.write_bytes(response.read())
    z=zipfile.ZipFile(archive);prefix=z.namelist()[0].split('/')[0]+'/'
    def original(path):return z.read(prefix+path)
    def svg(path):
        for _ in range(5):
            data=original(path)
            if data.lstrip().startswith(b'<'):return data.decode(),path
            path=posixpath.normpath(posixpath.join(posixpath.dirname(path),data.decode().strip()))
        raise ValueError('Unresolved upstream symlink '+path)
    d=BASE/'bibata-ice';cfg=tomllib.loads(original('configs/normal/win_rg.build.toml').decode())
    mapping={'arrow':'left_ptr','text':'xterm','wait':'wait','cross':'crosshair','uparrow':'right_ptr','handwriting':'pencil','sizenwse':'bd_double_arrow','sizenesw':'fd_double_arrow','sizewe':'sb_h_double_arrow','sizens':'sb_v_double_arrow','sizeall':'move','no':'circle','hand':'hand2','working':'left_ptr_watch','help':'question_arrow','pin':'pin','person':'person'}
    roles={};provenance=[]
    for role,name in mapping.items():
        options=cfg['cursors'][name];fallback=cfg['cursors']['fallback_settings']
        hx=options.get('x_hotspot',fallback['x_hotspot']);hy=options.get('y_hotspot',fallback['y_hotspot'])
        animated=name in ('wait','left_ptr_watch')
        group='shared' if name=='wait' else'modern'
        paths=[f'svg/groups/{group}/{name}/{name}-{i:02}.svg' for i in range(1,55)] if animated else[f'svg/modern/{name}.svg']
        images=[];hots=[]
        for path in paths:
            source,resolved=svg(path)
            source=source.replace('#00FF00','#FFFFFF').replace('#0000FF','#000000').replace('#FF0000','#FFFFFF')
            inner=E.fromstring(source);inner.set('width','256');inner.set('height','256')
            content,canvas=(64,96) if animated else(22,32);padding=(canvas-content)/2
            output=f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {canvas} {canvas}"><g transform="translate({padding} {padding}) scale({content/256})">'+E.tostring(inner,encoding='unicode')+'</g></svg>'
            images.append(output);hots.append((hx*content/256+padding,hy*content/256+padding))
            provenance.append({'role':role,'path':resolved,'sha256':hashlib.sha256(original(resolved)).hexdigest()})
        resource(d,role,images,hots,[1000/60]*len(images) if animated else None)
        roles[role]='.vani' if animated else'.vcur'
    write(d,'LICENSE',original('LICENSE').decode())
    write(d,'CREDITS.md',f'# Bibata Modern Ice\n\nAuthor: Abdulkaiz Khatri (ful1e5). Upstream: https://github.com/ful1e5/Bibata_Cursor/tree/{BIBATA}\n\nOriginal license preserved. Modifications: Ice colors, Regular Windows padding from upstream configuration, editable standalone frames/manifests. Original source hotspots and one-jiffy frame timing retained. Source preparation: build/ImportThemes.py.\n')
    write(d,'source-manifest.json',json.dumps({'commit':BIBATA,'files':provenance},indent=2)+'\n')
    write(d,'_upstream/win_rg.build.toml',original('configs/normal/win_rg.build.toml').decode())
    manifest(d,roles,'Bibata Ice')
def nordzy():
    url=f'https://raw.githubusercontent.com/guillaumeboehm/Nordzy-cursors/{NORDZY}/'
    d=BASE/'nordzy-white'
    cache=ROOT/'experiments/VectorRasterProbe/artifacts/sources'
    def fetch(relative):return urllib.request.urlopen(url+relative,timeout=30).read().decode()
    static=(cache/'nordzy-static.svg').read_text(encoding='utf-8') if (cache/'nordzy-static.svg').exists() else fetch('tools/svgs/themes/Nordzy-cursors-white.svg')
    spinner=(cache/'nordzy-spinner.svg').read_text(encoding='utf-8') if (cache/'nordzy-spinner.svg').exists() else fetch('tools/svgs/themes/Nordzy-cursors-white-spinner.svg')
    cc=fetch('tools/CC_build.json');config={x['cursor_name']:x for x in json.loads(cc)['data']}
    def slice_svg(source,key):
        doc=E.fromstring(source);rect=next(x for x in doc.iter() if x.tag.endswith('rect') and x.get('id')==key)
        x=float(rect.get('x','0'));y=float(rect.get('y','0'));w=float(rect.get('width'));h=float(rect.get('height'))
        parents={child:parent for parent in doc.iter() for child in parent};node=rect
        while node in parents:
            node=parents[node];tr=node.get('transform')
            if tr:
                match=re.fullmatch(r'translate\(\s*([-\d.]+)[,\s]+([-\d.]+)\s*\)',tr)
                if not match:raise ValueError('Unsupported atlas transform '+tr)
                x+=float(match[1]);y+=float(match[2])
        for parent in list(doc.iter()):
            for child in list(parent):
                if child.get('{http://www.inkscape.org/namespaces/inkscape}label')=='slices':parent.remove(child)
        doc.set('viewBox',f'{x} {y} {w} {h}');doc.set('width','64');doc.set('height','64')
        return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">'+E.tostring(doc,encoding='unicode')+'</svg>'
    mapping={'arrow':('left_ptr','default'),'text':('text','text'),'wait':('wait','wait'),'cross':('crosshair','crosshair'),'uparrow':('right_ptr','right_ptr'),'handwriting':('pencil','pencil'),'sizenwse':('nwse-resize','size_fdiag'),'sizenesw':('nesw-resize','size_bdiag'),'sizewe':('ew-resize','size_hor'),'sizens':('ns-resize','size_ver'),'sizeall':('move','fleur'),'no':('not-allowed','not-allowed'),'hand':('pointing_hand','pointer'),'working':('progress','progress'),'help':('help','help')}
    roles={}
    for role,(key,meta) in mapping.items():
        c=config[meta];animated=key in ('wait','progress')
        keys=[f'{key}-{i:02}' for i in range(1,17)] if animated else[key]
        images=[slice_svg(spinner if animated else static,k) for k in keys]
        resource(d,role,images,c['hotspots_64'],c['delays'] if animated else None)
        roles[role]='.vani' if animated else'.vcur'
    write(d,'LICENSE',fetch('COPYING'))
    write(d,'_upstream/Nordzy-cursors-white.svg',static);write(d,'_upstream/Nordzy-cursors-white-spinner.svg',spinner);write(d,'_upstream/CC_build.json',cc)
    write(d,'CREDITS.md',f'# Nordzy white\n\nAuthor: Guillaume Boehm and project contributors. Source: https://github.com/guillaumeboehm/Nordzy-cursors/tree/{NORDZY}\n\nOriginal GPL license retained. Modifications: standalone SVG slices wrapped, filters/colors and hotspot metadata preserved. Original source atlases/build metadata and editable frames included. Source preparation: build/ImportThemes.py.\n')
    manifest(d,roles,'Nordzy white')
def windows():
    d=BASE/'windows';roles={}
    for role,hot in [('wait',(32,32)),('working',(0,16))]:
        images=[(ROOT/f'experiments/AeroVectorProbe/artifacts/visual-03/{role}-{i:02}.svg').read_text(encoding='utf-8') for i in range(18)]
        resource(d,role,images,[hot]*18,[50]*18);roles[role]='.vani'
    manifest(d,roles,'Windows',False)
    write(d,'CREDITS.md','# Windows theme\n\nWait/Working use the original geometric recreation accepted for CursorShake, under the project MIT license. No original Windows cursor images are included. Other roles use verified resources of the local Windows installation.\n')
if __name__=='__main__':
    BASE.mkdir(parents=True,exist_ok=True);bibata();nordzy();windows()
    print('Prepared Bibata17 roles, Nordzy15 roles and Aero2 original vector animations.')
