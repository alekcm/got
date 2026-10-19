from PIL import Image, ImageDraw, ImageFont
ROWS = """
..............BBBBBBBBBBBBBB....................
............BBBBBBBBBBBBBBBBBB..................
..........NNNNNNNNNNNNNNNNNNNNNN................
.........NNNNNNNNNNNNNNNNNNNNNNNN...............
........NNNNNNNNNNNNNNNNNNNNNNNNNN..............
.......NNNNNNNNNNNNNNNNNNNNNNNNNNN.............
......NNNNNNNNNNNNNNNNNNNNNNNNNNNNN............
......NNNNNNNNNNNNNNNNNNNNNNNNNNNNN............
.......NNNNNNNNNNNNNNNNNNNNNNNNNNN.............
........NNNNNNNNNNNNNNNNNNNNNNNNN..............
..........NNNNNNNNNNNNNNNNNNNN..............VVV.
.............NNNNNNNNNNNNNN.............VVVVVVVV
.......II....RRRRRRRRRRRRRRR...........VVVVVVVVV
......III...RRRRRRRRRRRRRRRRRR.........VVVVVVVVV
......III..RRRRRRRRRRRRRRRRRRRR........VVVVVVVVV
...........RRRRRRRRRRRRRRRRRRRR.......VVVVVVVVVV
...........RRRRRRRRRRRRRRRRRRRRR......VVVVVVVVVV
..........WWWRRRRRRRRRRRRRRRRRRR.......VVVVVVVVVV
.........WWWWWWRRRRRRRRRCCCCCCC.......VVVVVVVVVV
........WWWWWWWWWWTTTTTTTCCCCCCC......VVVVVVVVVV
.......WWWWWWWWWWWTTTTTTTTCCCCCCC....SSSSSSSSSS
......WWWWWWWWWWWWTTTTTTTTTCCCCCC...SSSSSSSSSSS
......WWWWWWWWWWWWTTTTTTTTTTCCCCC..SSSSSSSSSSSS
.....WWWWWWWWWWWWWTTTTTTTTTTTT....SSSSSSSSSSSSS
....WWWWWWWWWWWWWWTTTTTTTTTTTT...SSSSSSSSSSSSSS
....WWWWWWWWWWWWWTTTTTTTTTTTTTT...SSSSSSSSSSSSS
.....WWWWWWWWWWWTTTTTTTTTTTTTTT....SSSSSSSSSSSS
......WWWWWWWWWTTTTTTTTTTTTTTTT.....SSSSSSSSSSS
.......WWWWWWWTTTTTTTTTTTTDDDDDD.....SSSSSSSSSS
........WWWWWTTTTTTTTTTTTDDDDDDDD....SSSSSSSSS.
.........WWWTTTTTTTTTTTDDDDDDDDDDDDDDDDDDDDDDD.
..........TTTTTTTTTTTDDDDDDDDDDDDDDDDDDDDDDDDDD
...........TTTTTTTTDDDDDDDDDDDDDDDDDDDDDDDDDDD.
................................................
""".strip("\n").splitlines()
ROWS = [r.ljust(max(len(x) for x in ROWS), ".") for r in ROWS]
W = max(len(r) for r in ROWS)
H = len(ROWS)
print(W, H, set(len(r) for r in ROWS))
COLORS = {
 'B':'#dfe9f2','N':'#9fb8c8','I':'#6f7f8c','R':'#7fb069','V':'#b7a6d6','W':'#d9b36c',
 'T':'#8fc97e','S':'#c9a35c','D':'#e0b96e','C':'#d98a8a','.':'#2f5d7c'
}
S=24
img = Image.new('RGB',(W*S,H*S),'#2f5d7c')
d = ImageDraw.Draw(img)
for y,row in enumerate(ROWS):
    for x,ch in enumerate(row):
        cx, cy = x*S+ (S//2 if y%2 else 0), y*S
        pts=[(x*S+S//2, y*S),(x*S+S, y*S+S//2),(x*S+S//2,y*S+S),(x*S,y*S+S//2)]
        d.polygon(pts, fill=COLORS.get(ch,'#000'), outline=(20,30,40))
        d.text((x*S+5,y*S+7), ch, fill=(0,0,0))
img.save('/home/user/got/tools/sketch_map.png')
