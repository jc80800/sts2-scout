"""Original synthetic UI, no game pixels. Requires Pillow; regenerate only on demand."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import argparse,base64,json
root=Path(__file__).resolve().parents[1];out=root/'tests/Scout.Tests/Fixtures'
parser=argparse.ArgumentParser();parser.add_argument('--font',default='/System/Library/Fonts/Supplemental/Arial.ttf');args=parser.parse_args()
font=ImageFont.truetype(args.font,26)
for style in ['plain','outlined']:
 im=Image.new('L',(960,540),30);d=ImageDraw.Draw(im)
 for x in range(100,860,12): d.rectangle((x,50,x+5,69),fill=230)
 regions=[]
 for x,name in zip([70,360,650],['Pommel Strike+','Vicious+','Cinder+']):
  d.rectangle((x,200,x+240,244),fill=160 if style=='outlined' else 255)
  d.text((x+12,207),name,font=font,fill=235 if style=='outlined' else 0,stroke_width=2 if style=='outlined' else 0,stroke_fill=50)
  regions.append(dict(x=x/960,y=200/540,width=240/960,height=45/540))
 pixels=list(im.getdata());(out/f'ocr-{style}.pgm').write_text('P2\n960 540\n255\n'+' '.join(map(str,pixels))+'\n')
 if style=='plain':
  r=dict(x=100/960,y=50/540,width=760/960,height=20/540)
  sample=[pixels[int((r['y']+(y+.5)/16*r['height'])*540)*960+int((r['x']+(x+.5)/32*r['width'])*960)] for y in range(16) for x in range(32)]
  calibration=dict(schemaVersion=1,version='synthetic-ocr-1',gameVersion='0.107.1',aspectRatio=960/540,threshold=.96,margin=.04,probes=[dict(kind='screen',screen='CardReward',slot=0,region=r,templates=[dict(label='reward',width=32,height=16,pixels=base64.b64encode(bytes(sample)).decode())])],rewardNameRegions=regions)
  (out/'ocr-calibration.json').write_text(json.dumps(calibration,indent=2)+'\n')
