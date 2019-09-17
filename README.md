# Automata

This is a fork of Margus Veanes's [Automata](https://github.com/AutomataDotNet/Automata) library.
The fork provided the foundation for the regex measurements used to characterize the regex corpuses in the [ASE'19 Regex Generalizability paper](http://people.cs.vt.edu/davisjam/downloads/publications/DavisMoyerKazerouniLee-RegexGeneralizability-ASE19.pdf).

## Summary of changes

- fixed several bugs in its automaton manipulations, eliminating long-running computation and memory exhaustion
- added support for generating the Chapman feature vector of a regex
- added support for collapsing certain expensive portions of a regex to facilitate simple path computation (e.g. converting a{1000} to a+ -- these regexes will have the same number of simple paths)
- added support for emitting an automaton’s graph in a format suitable for subsequent analysis
- introduced a command-line interface for automation

## Building

1. Obtain Visual Studio (I used Microsoft Visual Studio Community 2019, Version 16.0.1).
2. Open up the project. The "AutomataCLI" should be one of the components in the Solution explorer.
3. Select AutomataCLI in the build section (top of IDE), then go to Build and choose "Build Solution".
4. This should produce a `src/AutomataCLI/bin?Debug/AutomataCLI.exe` binary. You can run it from Linux using `wine AutomataCLI.exe`.

## Pre-built

The compiled version of AutomataCLI.exe used in the ASE'19 paper is available in the [artifact accompanying that paper](https://doi.org/10.5281/zenodo.3424960).