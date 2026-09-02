# RWU Fix Automatic Hydroponics Gravship

Локальный compatibility-патч для RimWorld 1.6, Automatic Hydroponics и Automatic Hydroponics Expanded.

- сохраняет `tickLeft`, длительность цикла и прогресс каждого процесса при взлёте и посадке гравилёта;
- очищает оставленный Vanilla Expanded Framework `cachedProcessStack`, чтобы сейв снова записывал живую очередь;
- после загрузки чинит незавершённый процесс с нулевым или отрицательным таймером, из-за которого урожай выдавался мгновенно;
- не меняет нормальную длительность выращивания и не трогает завершённые задания.

Загружать после Harmony, Vanilla Expanded Framework, Automatic Hydroponics и Automatic Hydroponics Expanded.
