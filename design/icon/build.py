#!/usr/bin/env python3
import subprocess, os, json, tempfile
from PIL import Image, ImageDraw, ImageFont
HERE=os.path.dirname(os.path.abspath(__file__)); ROOT=os.path.dirname(os.path.dirname(HERE))
tmp=tempfile.mkdtemp(); rend=os.path.join(tmp,"render")
subprocess.check_call(["swiftc","-O",os.path.join(HERE,"render.swift"),"-o",rend])
def render(svg,px):
    out=os.path.join(tmp,f"{os.path.basename(svg)}-{px}.png")
    subprocess.check_call([rend,os.path.join(HERE,svg),str(px),out]); return Image.open(out).convert("RGBA")
def app(px):
    return render("chesscoach-small.svg" if px<=32 else "chesscoach.svg",px)
iconset=os.path.join(ROOT,"ChessCoach/Resources/Assets.xcassets/AppIcon.appiconset"); os.makedirs(iconset,exist_ok=True)
images=[]
for pt in (16,32,128,256,512):
    for sc in (1,2):
        name=f"icon_{pt}x{pt}{'@2x' if sc==2 else ''}.png"
        app(pt*sc).save(os.path.join(iconset,name))
        images.append({"filename":name,"idiom":"mac","scale":f"{sc}x","size":f"{pt}x{pt}"})
json.dump({"images":images,"info":{"author":"xcode","version":1}},open(os.path.join(iconset,"Contents.json"),"w"),indent=2)
json.dump({"info":{"author":"xcode","version":1}},open(os.path.join(iconset,"../Contents.json"),"w"),indent=2)
wa=os.path.join(ROOT,"windows/ChessCoach.App/Assets"); os.makedirs(wa,exist_ok=True)
sizes=[16,24,32,48,64,128,256]
app(256).save(os.path.join(wa,"app.ico"),sizes=[(s,s) for s in sizes],append_images=[app(s) for s in sizes if s!=256])
def tray(color):
    out=[]
    for s in (16,20,24,32):
        m=render("tray.svg",s); a=m.split()[3]
        im=Image.new("RGBA",m.size,color+(0,)); im.putalpha(a); out.append(im)
    return out
for name,col in (("tray-light.ico",(32,32,32)),("tray-dark.ico",(255,255,255))):
    ims=tray(col); ims[-1].save(os.path.join(wa,name),sizes=[(i.width,i.height) for i in ims],append_images=ims[:-1])
sz=[16,20,24,32,48,64,128,256]
W=sum(s+24 for s in sz)+24; H=2*(256+120)
pv=Image.new("RGB",(W,H)); d=ImageDraw.Draw(pv)
for row,(bg,col,tc) in enumerate((((240,240,240),(32,32,32),(0,0,0)),((30,30,30),(255,255,255),(255,255,255)))):
    y0=row*(256+120); d.rectangle([0,y0,W,y0+256+120],fill=bg); x=24
    for s in sz:
        im=app(s); pv.paste(im,(x,y0+20+(256-s)//2),im)
        if s>64: x+=s+24; continue
        t=render("tray.svg",s); a=t.split()[3]; ti=Image.new("RGBA",t.size,col+(0,)); ti.putalpha(a)
        pv.paste(ti,(x,y0+256+30),ti); d.text((x,y0+256+30+s+4),str(s),fill=tc); x+=s+24
pv.save(os.path.join(HERE,"preview.png"))
z=Image.new("RGB",(16*8+32*8+60,32*8+20),(200,200,200))
for i,(s,x) in enumerate(((16,10),(32,16*8+30))):
    im=app(s).resize((s*8,s*8),Image.NEAREST); z.paste(im,(x,10),im)
z.save(os.path.join(HERE,"preview-zoom.png"))
