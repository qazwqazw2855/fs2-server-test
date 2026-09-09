# Architecture

God2 Classic Server is the single official C#/.NET 10 server. Login, character, world, runtime, and gameplay concerns are composed inside one ConsoleHost process.

This project follows `docs/PermanentArchitecturePolicy.md`: Official Restoration only, MariaDB as the only formal data authority, UTF-8/Unicode text, relative paths, one ConsoleHost, one BAT, and no gameplay feature switches for official fixed systems.

Official gameplay data must come from Official Client Recovery through the importer pipeline. Unverified data stays in official staging until primary key, foreign key, and reference validation pass.

Official Client Recovery follows `docs/OfficialClientRecoveryPermanentPolicy.md`: do not classify a category as missing because one file was not found; continue cross-reference recovery across DAT, Lua, Script, Resource, Localization, Model, Packet, Runtime, NPC, Quest, Dialog, Item, and Skill evidence.
