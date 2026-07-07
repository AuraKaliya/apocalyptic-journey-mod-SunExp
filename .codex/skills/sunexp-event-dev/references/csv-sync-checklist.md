# CSV Sync Checklist

Use this checklist before finishing event work.

## EventList Pairing

- Every `SunExp/Data/EventList/sunexp.csv` row needs a matching `SunExp/Text/EventList/sunexp.csv` row.
- If `1Script` exists, `1Describe` should exist and start with `<main>`.
- If `2Script` exists, `2Describe` should exist and start with `<main>`.
- Apply the same rule to options 3 and 4.

## Map Pairing

- Every custom `SunExp/Data/Map/sunexp.csv` row needs a matching `SunExp/Text/Map/sunexp.csv` row.
- Map-visible events should display through `Text/Map`, not reward helper captions.

## Script Calls

- Every event script call in `Data/EventList` should target a stable C# entry point, normally `CS.SunExp.Dll.Scripting.EventScripts.*`.
- Every called `EventScripts` method should exist in `SunExp-Dev/Scripting/EventScripts.cs`.
- Any remaining `SunExp_...(` script call is invalid; use a C# entry point instead.

## IDs

- Use full mod ids in script arguments, for example `SunExp_sunexp_morning_shard`.
- Official blessings may use official ids such as `blessing_8`.
- Story chain rows should use `Sub_` ids.

## CSV Quoting

- Quote English or Japanese text that contains commas.
- After editing text, import the CSV and verify option columns did not shift.
