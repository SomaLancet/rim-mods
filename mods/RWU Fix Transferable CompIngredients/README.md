# RWU Fix Transferable CompIngredients

Локальный compatibility-патч для RimWorld 1.6, защищающий списки переносимых предметов от повреждённой пары `CompIngredients` / `CompProperties`.

- патчит только `TransferableUtility.TransferAsOne`;
- перед штатным сравнением проверяет фактический тип `ThingComp.props` без вызова падающего `CompIngredients.Props`;
- если данные несовместимы, не объединяет эту пару предметов и оставляет экземпляры отдельными строками;
- если штатное сравнение всё же выбрасывает `InvalidCastException`, безопасно разъединяет только эту пару и записывает её реальные runtime-типы, категории и `defName`;
- один раз пишет в `Player.log` `defName`, ID предметов и фактический тип повреждённого `props`;
- нормальные предметы и штатную логику объединения не меняет.

Основной сценарий проверки: открыть формирование каравана и загрузку транспортников на сейве `second age — 8`. В логе не должны повторяться необработанный `InvalidCastException` и последующая ошибка Vehicle Framework `Ref 167D22CF`. Однократное предупреждение самого RWU-фикса ожидаемо и называет повреждённую пару.

Загружать после Harmony. Для предсказуемого порядка патч также объявляет optional `loadAfter` для Animals Gender on Caravan и Vanilla Nutrient Paste Expanded: Reimagined Progression.
