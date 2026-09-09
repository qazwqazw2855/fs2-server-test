# Dependency Rules

Allowed references:

- Domain references no project.
- Application references Domain only.
- Protocol references Domain and Application.
- Persistence references Domain and Application.
- Infrastructure references Application contracts only.
- Runtime references Domain, Application, and Protocol.
- ConsoleHost composes all projects.

Architecture tests validate project references, circular references, forbidden project names, and local absolute path leakage.
