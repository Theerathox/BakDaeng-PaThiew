# บักแดงพาเที่ยว (Bak Daeng Pa Thiew)

เกมแก้ปริศนาแนวด้านข้าง (2D side-scroller) — พานักท่องเที่ยวตาบอดหนีออกจากเมืองซอมบี้ในอีสาน **ด้วยสัตว์เลี้ยงตัวจริงของคุณ**

A 2D puzzle game: guide a blind traveller out of a zombie town in Isan, Thailand — **with your real pet**.

สร้างโดยทีม **CatCanFly** · Made by Team **CatCanFly**

## วิธีเล่น / How to play
- **สัตว์เลี้ยงหน้ากล้อง** บังคับบักแดง (หมานำทาง): AI ตรวจจับสัตว์ในภาพกล้องแล้วดูว่าสัตว์อยู่โซนไหน → เดินหน้า / หยุด / ถอย / วิ่ง
  - เงยกล้องให้สัตว์อยู่ต่ำในภาพ = **คลาน**
  - เอากล้องเข้าใกล้สัตว์ = **ดม** (หากุญแจที่ฝังอยู่)
- **เสียงผู้เล่น** "โฮ่ง~" ใส่ไมค์ = **เห่า** (เปิด/ปิดประตู โยกคันโยก เรียกเจ้านาย เปิดวิทยุหมอลำ)
  - ตะโกน "โฮ่ง!" ดังๆ = **โล่วิญญาณ** ปกป้องเจ้านาย 5 วินาที
- เล่นด้วยปุ่มบนจอก็ได้ ถ้าไม่มีสัตว์เลี้ยงหรือไมค์
- เป้าหมาย: พาเจ้านายไปถึงทางออกของแต่ละด่าน โดยไม่ให้ซอมบี้จับ — 11 ด่าน + 4 คัตซีน

Your pet in front of the camera moves the dog (AI detects which zone the pet is in). Your voice ("hooong") makes him bark; a loud shout raises a spirit shield. Get your blind master to the exit of each level without the zombies catching him.

## เปิดโปรเจกต์ / Open the project
1. ติดตั้ง **Unity 6000.0.58f2** (Unity 6) ผ่าน Unity Hub
2. Unity Hub → Add → เลือกโฟลเดอร์นี้
3. เปิดฉาก `Assets/Scenes/Title.unity` แล้วกด Play (ภาษาไทย/อังกฤษ เปลี่ยนได้ที่หน้าเมนู)
4. Build: Windows หรือ Android (WebGL ใช้ไมค์ไม่ได้)

Code: `Assets/Game` (gameplay, cutscenes, guide, Thai text), `Assets/Dog`, `Assets/Scripts/PetZoneDetector.cs` (pet camera + YOLO).
Level / cutscene scenes can be rebuilt from `Assets/Game/Editor/GameSceneBuilder.cs` (menu **Tools > Pet Game**).
Thai translations: `Assets/Game/LocThai.cs`.

## เครดิต / Credits
- เอนจินเกม / Engine: **Unity**
- AI ตรวจจับสัตว์เลี้ยง / Pet detection: **Ultralytics YOLO** (`Assets/Models/yolo26n.onnx`) — AGPL-3.0 — https://github.com/ultralytics/ultralytics
- ผู้ช่วยเขียนโค้ด / Coding help: **Claude** (Anthropic)
- ภาพ เพลง และเสียงบางส่วนสร้างด้วยเครื่องมือ AI / Some art, music and sounds were made with the help of AI tools
- ฟอนต์ไทย / Thai font: **Kanit** by Cadson Demak — SIL Open Font License 1.1 (`Assets/Fonts/Kanit-OFL.txt`). The copy in `Assets/Resources/Fonts/KanitGame.ttf` is modified (extra Thai tone-mark glyphs) and renamed, as the OFL requires.

## ไลเซนส์ / License
ซอร์สโค้ดของเกมนี้เผยแพร่ภายใต้ **GNU AGPL-3.0** (ดูไฟล์ `LICENSE`) เพราะเกมใช้ Ultralytics YOLO ซึ่งเป็น AGPL-3.0
This game's source code is released under the **GNU Affero General Public License v3.0** (see `LICENSE`), because it uses Ultralytics YOLO (AGPL-3.0).
The Kanit font stays under the SIL Open Font License.
