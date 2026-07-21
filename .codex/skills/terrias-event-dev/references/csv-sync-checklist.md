# CSV Sync Checklist

Use this checklist before finishing event work.

## EventList Pairing

- Every `Terrias/Data/EventList/terrias.csv` row needs a matching `Terrias/Text/EventList/terrias.csv` row.
- If `1Script` exists, `1Describe` should exist and start with `<main>`.
- If `2Script` exists, `2Describe` should exist and start with `<main>`.
- Apply the same rule to options 3 and 4.

## Map Pairing

- Every custom `Terrias/Data/Map/terrias.csv` row needs a matching `Terrias/Text/Map/terrias.csv` row.
- Map-visible events should display through `Text/Map`, not reward helper captions.

## Script Calls

- Every event script call in `Data/EventList` should target a stable C# entry point, normally `CS.Terrias.Dll.Scripting.EventScripts.*`.
- Every called `EventScripts` method should exist in `Terrias-Dev/Scripting/EventScripts.cs`.
- Any remaining `Terrias_...(` script call is invalid; use a C# entry point instead.

## IDs

- Use full mod ids in script arguments, for example `Terrias_terrias_morning_shard`.
- Official blessings may use official ids such as `blessing_8`.
- Story chain rows should use `Sub_` ids.

## CSV Quoting

- Quote English or Japanese text that contains commas.
- After editing text, import the CSV and verify option columns did not shift.
