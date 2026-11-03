# Mono I18N

`I18N.dll` and `I18N.CJK.dll` are Mono's code-page encodings, unmodified, copied from
Unity 6000.6.8f1 (`Editor/Data/MonoBleedingEdge/lib/mono/unityjit-win32`). Unity only puts
I18N assemblies in a player build when the project contains them, and without them
`Encoding.GetEncoding(932)` fails in the player, so Shift-JIS TJA and box.def files were
read as broken UTF-8. The Editor has them either way, which is why tests did not notice.
`link.xml` keeps IL2CPP stripping from removing the encodings Mono creates by reflection.

| File | SHA-256 |
| --- | --- |
| I18N.dll | c6f4906cd5d5eb7c90366cfee256475b20e398e031661bc39fc2b0a3c9fc4a2b |
| I18N.CJK.dll | f1dcd5286609161d0c9f02e56e5481f776f0ed6329b86bf0da6076c72c88cae9 |

Mono's class libraries are MIT-licensed; see LICENSE.txt and <https://github.com/mono/mono>.
