# The JSON probe

```powershell
.\count.ps1 -Package Newtonsoft.Json -Version 13.0.3
```

It creates a throwaway `net48` class library in the temporary directory, adds the one package,
builds it, prints what landed in the output beside the project's own assembly, and deletes
everything. Nothing is installed and nothing is left behind.

`counts.json` holds what each candidate measured, with the version beside it, because an assembly
count is a fact about a version rather than about a library.

Two questions are being asked at once, and they have different answers per candidate: what it costs
to carry, and whether it reads JSON, JSON Schema, or both. A schema validator that also drags a JSON
reader is paying twice.
