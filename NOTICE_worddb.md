# The word lists of the keyboard — where they come from

`worddb_FR`, `worddb_IT` and `worddb_ES` (French, Italian, Spanish) are word-frequency lists with the words that commonly follow each word. They
were made with `tools/WordDbBuilder` (see its README) from two collections of text. Only counts of words and of pairs of words are in the lists,
no sentences.

| Source | What it gives | Terms |
|---|---|---|
| **Tatoeba** sentences (https://tatoeba.org), the French, Italian and Spanish sentences, exported 2026-10-17 | everyday sentences: most of the weight (96 %) | Creative Commons Attribution 2.0 France (CC BY 2.0 FR); some sentences CC0. Attribution: *Tatoeba and its contributors* |
| **Europarl** v7 (https://www.statmt.org/europarl/), the French, Italian and Spanish sides of the parallel corpora | the general vocabulary, names and word pairs of formal language: 4 % of the weight | "We are not aware of any copyright restrictions of the material" (the corpus page asks to cite Koehn, P. (2005): *Europarl: A Parallel Corpus for Statistical Machine Translation*, MT Summit X) |

Not used on purpose: sources under Creative Commons Attribution-ShareAlike (Wikipedia, FrequencyWords, Lexique, wordfreq), because the keyboard may not be
used commercially by others and ShareAlike would not allow that restriction on what is made from them.

A short list of vulgar words is left out of each list (`tools/WordDbBuilder/blocklist_*.txt`); it has not been read by a native speaker yet.

The Dutch and English lists (`worddb_NL`, `worddb_EN`) are older; their sources are not recorded here (the Dutch one was made from a newspaper corpus,
mostly *Wablieft*).
