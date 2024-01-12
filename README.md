# BpsNET

BpsNET is a .Net library used to create and apply BPS patches onto files.

## Installation

Use NuGet to install BpsNet.

## Usage

```csharp
// Applying a patch
byte[] original = File.ReadAllBytes("game.bin");
var patch = new BpsPatch(File.ReadAllBytes("game.bps"));
byte[] patched = patch.Apply(original);

// Creating a patch
byte[] original = File.ReadAllBytes("game.bin");
byte[] modified = File.ReadAllBytes("game_modified.bin");
var patch = BpsPatch.Create(original, modified, "metadata");
byte[] patchData = patch.GetBytes();
```

## Contributing

Pull requests are welcome.

## License
[MIT](LICENSE)
