rugptools
---------

Tools for working with rUGP games. Currently includes a C# WPF GUI tool and a C# .rio file access library.

Currently supports the initial DVD release of MLA; probably doesn't work with anything else.

Currently you can view background images (no character art), and hex dumps of objects.

Issues
======

  - Object deserialization is not completely bug-free, there are still objects it chokes on.

  - The "Find All Images" analysis uses a very large amount of memory.

Acknowledgements
================

rugptools was developed using hiko_bae's alterdec as a reference, and includes
code transcribed from it. All other code is licensed under the GPLv2+ or later.

Thanks to hiko_bae for alterdec as this work wouldn't have been possible without it.

TODO
====

  - Integrate chinesize's improvements to alterdec.
