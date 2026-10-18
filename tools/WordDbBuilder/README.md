# WordDbBuilder

Makes a word-list source `worddb_XX.xml` (the format of `worddb_NL.xml`) from text corpora. The build turns it into `worddb_XX.wfq` (`tools/ConvertWordDb.ps1`).
Where the French, Italian and Spanish lists come from: `NOTICE_worddb.md` in the project folder.

## Use

```
dotnet build tools/WordDbBuilder/WordDbBuilder.csproj
dotnet run --no-build --project tools/WordDbBuilder -- --lang fr --top 20000 --next 10 \
   --musthave tools/WordDbBuilder/musthave_fr.txt --seed tools/WordDbBuilder/musthave_fr.txt \
   --blocklist tools/WordDbBuilder/blocklist_fr.txt --cache C:/dev/worddb_bronnen/fr/cache \
   --out worddb_FR.xml --report report.txt \
   --source "tatoeba:C:/dev/worddb_bronnen/fr/tatoeba/fra_sentences.tsv:tatoeba:0.96" \
   --source "europarl:C:/dev/worddb_bronnen/fr/europarl/europarl-v7.fr-en.fr:lines:0.04"
```

A source is `name:path:format:weight`; the format is `tatoeba` (tab separated: id, language, sentence) or `lines` (one sentence per line). The first source is the
everyday one (it decides which words are enough, and names count only by the others).

| Option | Meaning |
|---|---|
| `--lang` | `fr`, `it`, `es` (elisions and one-letter words per language, see `Rules`) |
| `--top`, `--next` | number of words (20000) and next words per word (10; the keyboard reads at most 10) |
| `--seed` | words that must be there: those that rank lower than `--seed-rank` (2500) are raised to that rank |
| `--musthave` | the quality gate: every word of every phrase must be in the first 3000 (reported, not enforced) |
| `--blocklist` | words that are never suggested |
| `--cache` | folder for what was counted: a run with other weights, seeds or blocked words then takes seconds instead of ~20 minutes |
| `--sample-every K` | read one line in K (a quick try) |
| `--selftest` | only the self-test (accents, elisions, hyphens, sentence starts) |

## What it does

* A word is a run of letters; a hyphen ends it (so does the keyboard); a digit makes a break; acronyms are left out.
* An apostrophe inside a word is kept, but an **elision** (`l'`, `qu'`, `dell'` …) is split off as a word of its own ending in an apostrophe: the keyboard treats
  such words that way (`WordPredictor.TryApostropheInWord`). `aujourd'hui` stays one word.
* Words are lower-cased unless they are mostly written with a capital in the middle of a sentence (names). A name counts only by the later sources.
* A form without accents (`etait`) is left out when the accented one (`était`) is five times as frequent.
* Tatoeba: exact repeats are skipped and of the sentences that start with the same three words only 60 are used (made-up sentence floods).
* Frequencies and word pairs are blended per million words with the weights of the sources.
* The first self-test failure stops the run.
