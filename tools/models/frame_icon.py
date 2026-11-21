"""Crop a render so the outline of <frame png> fills FILL of a square, centered, then save a premultiplied 128 px PNG.
    python frame_icon.py <render.png> <frame.png> <out.png> [fill]"""
import os, sys
from PIL import Image
src, frame, out = sys.argv[1:4]
fill = float(sys.argv[4]) if len(sys.argv) > 4 else 0.75
im = Image.open(src).convert('RGBA')
b = Image.open(frame).convert('RGBA').getchannel('A').point(lambda v: 255 if v > 24 else 0).getbbox()
side = round(max(b[2] - b[0], b[3] - b[1]) / fill)
x0, y0 = round((b[0] + b[2] - side) / 2), round((b[1] + b[3] - side) / 2)
canvas = Image.new('RGBA', (side, side), (0, 0, 0, 0)); canvas.paste(im, (-x0, -y0))
full = im.getchannel('A').point(lambda v: 255 if v > 24 else 0).getbbox()
clip = full[0] < x0 or full[1] < y0 or full[2] > x0 + side or full[3] > y0 + side
r = canvas.convert('RGBa').resize((128, 128), Image.LANCZOS)
Image.frombytes('RGBA', r.size, r.tobytes()).save(out)
c = Image.open(out).getchannel('A').point(lambda v: 255 if v > 24 else 0).getbbox()
print(os.path.basename(out), 'whole-model fill', round(max(c[2] - c[0], c[3] - c[1]) / 128, 3), 'clipped at edge' if clip else 'fits')
