# PyStandNet

Python independent deployment environment.
C# version of the repository PyStand[https://github.com/skywind3000/PyStand].

# Usage

Just the same as PyStand[https://github.com/skywind3000/PyStand].

# Build

Support NativeAOT, but just testing on Windows10 .NET8.0.100-preview.5 now.
``` bash
dotnet publish -r win-x64 -c Release -p:PublishAot=true
```

# TODO

- ⚠<span style="color:red;">ERROR</span>: Now count not run correctly. With ERROR "KeyError: 'PYSTAND'" by python. Maybe "Environment.SetEnvironmentVariable" didn't work correctly.
- AOT: test on WSL.