# Генератор карт в стиле Songs of Conquest

Документ отвечает на два вопроса: **как устроена генерация карт в SoQ** и **как она
воспроизведена в «Железном троне»**. Код — `Assets/IronThrone/Scripts/Core/Worldgen/`,
сцена — `Assets/Scenes/SoQMap.unity`, данные — `Resources/IronThroneData/MapGen/`.

## 1. Как это работает в Songs of Conquest

Официальная спецификация — <https://www.songsofconquest.com/modding/rmg>. Карта в SoQ
описывается **двумя JSON-файлами**, которые лежат модом в
`%userprofile%\AppDataLocalLow\Lavapotion\SongsOfConquest\Mods`:

### 1.1 Layout — «где что находится»

```jsonc
{
  "type": "random-map-generator-layout",
  "name": "My layout",
  "blueprint": "my_blueprint",
  "playerCount": 4,
  "size": 64,                       // 40 / 64 / 128 / 160 / 256
  "gameMode": [0, 1, 2],
  "layouts": [                      // варианты (саблayout'ы) — выбираются случайно
    {
      "name": "Variant A",
      "nodes": [
        {
          "id": 1,
          "point": { "x": [0.08, 0.22], "y": [0.06, 0.22] }, // доля карты (диапазон!)
          "fill": 0.07,               // доля суши под регион
          "player": 1,                // 0 — ничейный
          "group": 1,                 // регионы одной группы одинакового размера
          "region_identifier": ["player_start"],
          "primary_building": ["AgnosticTown"],
          "difficulty": [1, 2],
          "biome": "north",           // расширение Iron Throne
          "children": [
            { "id": 11, "fill": 0.03, "region_identifier": ["player_support"],
              "road": 1, "guardChance": 0.2, "guardTier": ["Low"],
              "parentGrowthBeforeBirth": 1.0 }
          ]
        }
      ],
      "connections": [
        { "nodeFrom": 1, "nodeTo": 2, "road": 2, "guardChance": 0.5,
          "guardTier": ["Medium"], "wide": true, "group": 0 }
      ]
    }
  ]
}
```

Ключевые правила:

- **`point` — доля карты, а не пиксели.** `x: [0.08, 0.22]` означает «где-то в этой
  полосе»; генератор выбирает точку внутри диапазона. Поэтому регион и его содержимое
  каждый раз разные.
- **`fill` — доля суши региона.** Регионы растут от точки-зерна в стороны, пока не
  наберут свою долю; в одной `group` они растут синхронно (одинаковый размер).
- **`children` — вложенные регионы.** Они рождаются у границы родителя после того, как
  он вырос; `parentGrowthBeforeBirth` задаёт порог, `road` — тип дороги к родителю.
- **`connections`** прокладывают дороги и/или охраняемые проходы между узлами.
  `road`: 0 — без дороги, 1 — грунтовка, 2 — мостовая; `wide` — широкий проход.
- **`guardTier`**: `Random | Low | Medium | High`; `guardChance` — шанс, что проход
  охраняется.
- **`primary_building`**: что стоит в центре региона (`AgnosticTown`,
  `LargeSettlement`, `SmallSettlement`, `GoldMine`, `Beacon`, `StoneOfWinning`,
  `ArcaneSpire`…).
- `spawnChance` позволяет региону иногда не появляться; `largeCenterArea` — широкая
  центральная площадка; `isClaimed` — регион уже занят.

### 1.2 Blueprint — «что именно лежит в регионе»

```jsonc
{
  "type": "random-map-generator-blueprint",
  "name": "my_blueprint",
  "startingResources": { "gold": 3000, "wood": 6, "stone": 6 },
  "regions": [
    {
      "identifier": "player_start",
      "iterations": 1,
      "shuffle": true,
      "entries": [
        {
          "objects": ["GoldMine", "WoodMine", "StoneMine"],
          "type": "random",          // random — любой из списка
          "count": [2, 3],           // число или [min, max]
          "distance": 0.2,           // минимум от центра (доля радиуса)
          "difficulty": [1, 2],
          "guardChance": 0.4,
          "guardTier": ["Low", "Medium"],
          "location": ["NearRoad", "OutInTheOpen"],
          "children": [{ "objects": ["PileOfGold"], "count": 2 }]
        }
      ]
    }
  ]
}
```

Регион получает идентификатор (`player_start`, `player_support`, `treasure_low`,
`treasure_medium`, `treasure_high`, `center`, `beacon`, `default`, `fill`…), и blueprint
заполняет его объектами по записям. `default` — резерв и придорожные объекты; `fill` —
то, чем добивается пустая земля. Гварды ставятся отдельными отрядами рядом с объектом,
поэтому игрок видит, что объект охраняется.

### 1.3 Порядок генерации (как в SoQ)

1. Выбирается саблayout (вариант карты).
2. Строится **маска суши** — континент с береговой линией и островами.
3. Регионы получают точки-зёрна и **растут по `fill`**; группы растут синхронно;
   дочерние регионы рождаются у границ.
4. Через регионы прокладываются **дороги/проходы** по `connections`.
5. В центрах ставятся **главные строения**; в стартовых регионах — города игроков.
6. **Blueprint** заполняет регионы объектами с учётом `count`, `distance`,
   `location`, `guardChance`, `guardTier`.
7. Свободная земля добивается мелкими находками.
8. Всё это сохраняется вместе с сидом: **один сид + одни настройки = одна и та же карта**.

## 2. Что сделано в «Железном троне»

### 2.1 Код

| Файл | Назначение |
|---|---|
| `Core/Worldgen/SoqRng.cs` | Детерминированный SplitMix64 + value-noise. Не зависит от `System.Random`. |
| `Core/Worldgen/AdventureMap.cs` | Модель карты: тайлы, регионы, объекты, старты, отчёт генерации. |
| `Core/Worldgen/SoqRmgDefs.cs` | DTO layout/blueprint/узлов/связей (поля совпадают с SoQ). |
| `Core/Worldgen/SoqRmgLoader.cs` | Чтение JSON layout/blueprint/каталога объектов. |
| `Core/Worldgen/AdventureCatalog.cs` | Каталог объектов и спрайтов; варианты по биому. |
| `Core/Worldgen/AdvTerrainInfo.cs` | 16 классов рельефа, биомы, правила выбора тайла. |
| `Core/Worldgen/SoqMapGenerator.cs` | Сам конвейер (шаги 1–7 выше). |
| `Core/Worldgen/SoqMapCheck.cs` | Проверки: детерминизм, различимость сидов, связность, старты. |
| `Unity/SoqMap/SoqMapView.cs` | 2.5D-отрисовка: земля, вода, дороги, границы, стенди, миникарта. |
| `Unity/SoqMap/SoqMapScene.cs` | Сцена: сид, layout, размер, доля суши, отчёт, выбор тайла. |

Генератор — **чистый C#**: его можно вызывать из headless-харнесса, из редактора и из
Unity одинаково. Unity-слой только рисует.

### 2.2 Данные

`Resources/IronThroneData/MapGen/`:

- `layout_duel.json` — 2 игрока, 48×48, два варианта («Перекрёсток», «Две реки»);
- `layout_ring.json` — 4 игрока, 64×64, два варианта («Четыре ветра», «Ромб»);
- `blueprint_default.json` — правила заполнения всех идентификаторов;
- `objects.json` — каталог объектов: имя, категория, спрайт, заглушка, масштаб,
  варианты по биому, лимиты.

Формат layout/blueprint **совместим с SoQ**: можно положить официальный файл мода в
`MapGen`, добавить `"id"` и `"biome"` — генератор его подхватит.

### 2.3 Арт и декор

Атласы (5 файлов, 57 нарезок) собирает `tools/soq_bake_sprites.py` из дампа SoQ:

| Атлас | Что внутри |
|---|---|
| `soq_objects` | шахты (7 фракционных вариантов золотой + янтарь/камень/дерево/небесная руда/мерцающая ткань), фермы, мельницы, руины, каменный монумент, шпиль, маяки, портал |
| `soq_settlements` | города (4 биома), большие и малые поселения, лагеря, таверна |
| `soq_nature` | чардрево, нора, золотой ручей, грибы |
| `soq_units` | стражи (бандит, лучник, волк, главарь) |
| `soq_decor` | деревья для пустой земли (7 вариантов) |

Декор — отдельный слой: `AdventureMap.Decor`. Он ставится после объектов на
свободные тайлы, не занимает клетку и не мешает находкам, поэтому карта перестаёт
быть «лысой» (в SoQ суша всегда чем-то заросла). Плотность зависит от рельефа: лес —
густо, пески — редко. В сцене есть переключатель «Деревья на карте».

Чего в дампе нет — заменено заглушками (`PlaceholderBase*`, `PlaceholderBuilding*`):
сундуки, кучи ресурсов, артефакты. Их легко заменить: добавить спрайт в атлас, запись
в `soq_sprite_slices.json` и поле `sprite` у объекта в `objects.json`.

### 2.4 Сцена

`Assets/Scenes/SoQMap.unity` добавлена в Build Settings. Вход — кнопка
**«Карта SoQ (генератор)»** в главном меню.

В сцене:

- поле **сида** (число или текст — текст хешируется стабильно), кнопки
  «Сгенерировать» и «Случайный»;
- выбор **layout'а** и **размера** (32/48/64/96), регулировка **доли суши**;
- статистика: тайлы, области, объекты, охраняемые, дороги, старты;
- **отчёт генерации** (что не поместилось и почему);
- клик по карте — информация о тайле, области и объекте;
- переключатель деревьев и отчёт генерации;
- миникарта, метки стартов игроков;
- камера: WASD/стрелки, колесо, средняя кнопка. `R` — перегенерировать, `N` — новый
  сид, `Esc` — в меню.

## 3. Проверка без Unity

```bash
# детерминизм + качество карт (нужен .NET)
cd Tools/Headless
dotnet run -- ../../Assets/IronThrone/Resources/IronThroneData --soqmap --seeds 6

# визуальный предпросмотр без Unity (нужен Pillow)
python3 tools/soq_mapgen_preview.py --seeds 1,2,3
# -> tools/soq_mapgen_contact.png и tools/soq_<layout>_seed<N>.png

# предпросмотр с настоящим артом (тайлы, спрайты объектов, деревья)
python3 tools/soq_mapgen_art_preview.py --seeds 1,7 --tile 26
# -> tools/soq_art_preview.png и tools/soq_art_<layout>_seed<N>.png
```

`soq_mapgen_art_preview.py` — независимая копия конвейера на Python: она нужна, чтобы
проверять вид карты (и правки баланса/рельефа) без запуска Unity. Готовый результат
лежит в `tools/soq_art_preview.png` (2 layout'а × 2 сида), примеры — в
`tools/soq_art_duel_seed1.png` и `tools/soq_art_ring_seed1.png`.

`--soqmap` проверяет:

- одинаковый сид дважды даёт одинаковую сигнатуру карты;
- разные сиды дают **разные** карты;
- суша связна, у каждого тайла есть регион;
- есть стартовые города и ресурсы в стартовых областях;
- объекты стоят на суше и не накладываются друг на друга;
- декор (деревья) стоит на свободной суше и не перекрывает объекты.

## 4. Как расширять

- **Новый layout**: скопируйте `layout_duel.json`, поменяйте `id`, `size`,
  `playerCount`, точки и `fill`. Перезапустите сцену — layout появится в списке.
- **Новый тип объекта**: добавьте запись в `objects.json` и используйте её `id` в
  blueprint'е.
- **Новый биом**: `AdvBiome` + `AdvTerrainInfo.Pick` + ключ в `AdvObjectDef.BiomeKey`
  (для вариантов спрайтов).
- **Больше объектов в регионе**: увеличьте `count`/`iterations` в blueprint'е; следите,
  чтобы `distance` не отсекал все тайлы маленькой области.
- **Гуще/реже деревья**: `SoqMapGenerator.DecorDensity` (плотность по типу рельефа) и
  `CATEGORY == "decor"` в `objects.json` (набор вариантов дерева).
