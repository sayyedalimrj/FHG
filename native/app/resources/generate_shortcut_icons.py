#!/usr/bin/env python3
"""Generate three original, distinct multi-resolution FHG Windows shortcut icons.

No external Python modules required. Each icon is a 7-size PNG-in-ICO
resource. No trademarked Firefox/OpenAI logo art is copied. The blue/orange
browser icon has a small readable GPT corner badge; account and proxy icons
have distinct teal/purple silhouettes.
"""
import math
import struct
import sys
import zlib
from pathlib import Path

SIZES = (16, 24, 32, 48, 64, 128, 256)
GLYPHS = {
    "G": ("01110","10000","10110","10010","10010","10010","01110"),
    "P": ("11110","10001","10001","11110","10000","10000","10000"),
    "T": ("11111","00100","00100","00100","00100","00100","00100"),
}

def circle(x, y, cx, cy, radius):
    return (x-cx)**2 + (y-cy)**2 <= radius*radius

def segment(x, y, a, b, width):
    ax, ay = a
    bx, by = b
    dx, dy = bx-ax, by-ay
    dot = max(0.0, min(1.0, ((x-ax)*dx+(y-ay)*dy)/(dx*dx+dy*dy+1e-12)))
    return (x-ax-dot*dx)**2 + (y-ay-dot*dy)**2 <= (width/2)**2

def text_pixel(x, y, string, start_x, start_y, scale):
    for index, ch in enumerate(string):
        if ch not in GLYPHS:
            continue
        px = int((x-start_x-index*6*scale)//scale)
        py = int((y-start_y)//scale)
        if 0 <= px < 5 and 0 <= py < 7 and GLYPHS[ch][py][px] == "1":
            return True
    return False

def shade(n, kind):
    pixels = bytearray()
    for yi in range(n):
        pixels.append(0)  # PNG RGBA filter type None
        for xi in range(n):
            x, y = (xi+.5)*100/n, (yi+.5)*100/n
            # Rounded square tile.
            bx = max(abs(x-50)-39, 0)
            by = max(abs(y-50)-39, 0)
            if bx*bx+by*by > 9**2:
                pixels.extend((0,0,0,0))
                continue
            t = max(0, min(1, (x+y)/180))
            if kind == "browser":
                rgb = (int(16+11*t), int(43+17*t), int(81+21*t))
                # Orange moving flame in a circular orbit of blue globe.
                radius = math.hypot(x-47,y-49)
                angle = (math.atan2(y-49,x-47) + 2*math.pi)%(2*math.pi)
                if 27 < radius < 39 and 0.48 < angle < 4.45:
                    rgb = (255, int(169-45*t), int(50+15*t))
                if circle(x,y,47,49,27):
                    rgb = (int(28+12*t), int(136+30*t), int(214+21*t))
                    if 23.5 < radius < 26.5: rgb = (126,211,250)
                    if abs(x-47)<1.7 or abs(y-49)<1.7:
                        rgb = (102,200,247)
                    if abs(x-47)<14 and 12 < abs(y-49)<14:
                        rgb = (95,190,246)
                # Stylized flamelike head/top tail, no trademarked artwork.
                if segment(x,y,(25,39),(47,19),8) or segment(x,y,(24,40),(24,54),9):
                    rgb = (252,121,42)
                # GPT badge at lower-right, deliberately not an OpenAI logo.
                if circle(x,y,77,77,18):
                    rgb = (109,66,225)
                    if text_pixel(x,y,"GPT",59.4,72.2,1.25):
                        rgb = (255,255,255)
            elif kind == "proxy":
                rgb = (int(37+12*t), int(25+8*t), int(83+20*t))
                # Network triangle: proxy traffic through distinct nodes.
                for a,b in [((27,68),(50,33)),((50,33),(73,65)),((27,68),(73,65))]:
                    if segment(x,y,a,b,6): rgb = (176,143,255)
                for cx,cy,r in [(27,68,12),(50,33,13),(73,65,12)]:
                    if circle(x,y,cx,cy,r): rgb = (162,89,244)
                    if circle(x,y,cx,cy,r-5): rgb = (237,226,255)
                if circle(x,y,80,22,10):
                    rgb = (91,59,196)
                if segment(x,y,(75,22),(85,22),3) or segment(x,y,(80,17),(80,27),3):
                    rgb = (240,233,255)
            else:
                rgb = (int(13+10*t), int(73+19*t), int(78+10*t))
                # Two distinct account silhouettes.
                if circle(x,y,48,36,15):
                    rgb = (200,255,235)
                if circle(x,y,48,38,9):
                    rgb = (24,130,121)
                if circle(x,y,67,44,11):
                    rgb = (130,232,196)
                if circle(x,y,67,46,6):
                    rgb = (22,106,107)
                if 29<x<70 and 57<y<80 and circle(x,y,49,75,24):
                    rgb = (200,255,235)
                if 62<x<83 and 58<y<76 and circle(x,y,70,69,16):
                    rgb = (130,232,196)
                if 33<x<64 and 66<y<80 and circle(x,y,49,82,15):
                    rgb = (24,130,121)
            pixels.extend((*rgb,255))
    return bytes(pixels)

def png_blob(n, kind):
    def chunk(tag, payload):
        return (struct.pack(">I",len(payload))+tag+payload+
                struct.pack(">I",zlib.crc32(tag+payload)&0xffffffff))
    return (b"\x89PNG\r\n\x1a\n"+
            chunk(b"IHDR",struct.pack(">IIBBBBB",n,n,8,6,0,0,0))+
            chunk(b"IDAT",zlib.compress(shade(n,kind),9))+
            chunk(b"IEND",b""))

def write_ico(path, kind):
    blobs = [png_blob(n,kind) for n in SIZES]
    stream = bytearray(struct.pack("<HHH",0,1,len(SIZES)))
    offset = 6+16*len(SIZES)
    for n,blob in zip(SIZES,blobs):
        stream.extend(struct.pack("<BBBBHHII",n if n<256 else 0,
                                  n if n<256 else 0,0,0,1,32,len(blob),offset))
        offset += len(blob)
    for blob in blobs: stream.extend(blob)
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(stream)
    print(f"PASS: {path.name} ({kind}), {len(SIZES)} sizes, {len(stream)} bytes")

def main(directory):
    output = Path(directory)
    for name,kind in (
        ("FHGFirefox.ico","browser"),
        ("FHGAccounts.ico","accounts"),
        ("FHGProxy.ico","proxy")):
        write_ico(output/name,kind)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv)>1 else "native/original-ui/Assets")
