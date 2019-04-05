[![Gitter](https://badges.gitter.im/IL2X/community.svg)](https://gitter.im/IL2X/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge)

# IL2X (Currently Experimental)
Translates .NET IL to supported and unsuported .NET platforms and architectures. (Powered by Mono.Cecil)<br>
If you're looking for CS2X: https://github.com/reignstudios/CS2X

## Goals
This project will focus on translating .NET IL for non-supported .NET targets. Portibility is a huge focus.
* .NET Standard compatibility
* Native C performance
* C89: modern, legacy and embedded platforms (x86, MIPS, SPARK, RISC-V, PPC, AVR, etc)
* CC65: 6502 platforms (Atari, C64, NES, Apple II, etc) [CS2X may be better suited]]
* SDCC: Many targets (ColecoVision, etc) [CS2X may be better suited]
* Assembly: CP1610 (Intellivision) [CS2X may be better suited]]
* Retarget: Custom assembly targets via plugin system (FPGA CPU, 16bit bytes, etc)
* Custom Standard lib(s) for various targets.
* Documentation

## Project libraries
* IL2X.Core: .NET IL translator/compiler lib
* IL2X.CLI: CLI interface for IL2X.Core
* IL2X.Portable.CoreLib: The IL2X runtime system CoreLib

## What IL2X does .NET Core / CoreRT doesn't
You're asking yourself why this project exists?
* True portability. You don't need to add explicit support for each target as portable C does that already.
* Statically link the entire programs dependencies. Each ".dll" is translated to a ".h" file and each ".exe" is translated to a ".c" file that includes all its dependencies as headers. This allows you to ship a single lightweight binary with no external libraries. Later support for closed source dynamic linked libs will be supported.
* IL2X can be significantly faster than all currently available .NET runtimes when it comes to heavy number crunching thanks to the awesome C optimizers available today.
* Allows you to directly invoke C methods statically for better optimizations.
* Supports many C compilers allowing you to choose what's best.