# IL2X (Currently Experimental)
Translates .NET IL to supported and unsuported .NET platforms and architectures. (Powered by Mono.Cecil)<br>
If you're looking for CS2X: https://github.com/reignstudios/CS2X

## Goals
This project will focus on translating .NET IL for non-supported .NET targets.
* Native C performance
* C89: modern, legacy and embedded platforms (x86, MIPS, SPARK, RISC-V, PPC, AVR, etc)
* CC65: 6502 platforms (Atari, C64, NES, Apple II, etc) [CS2X may be required]
* SDCC: Many targets (ColecoVision, etc) [CS2X may be required]
* Assembly: CP1610 (Intellivision) [CS2X may be required]
* Retarget: Custom assembly targets via plugin system (FPGA CPU, 16bit bytes, etc)
* Custom Standard lib(s) for various targets.

## Project libraries
* IL2X.Core: .NET IL translator/compiler lib
* IL2X.CLI: CLI interface for IL2X.Core