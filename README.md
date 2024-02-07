# <a href="https://discord.gg/QmJEGER9An"><image src="https://theme.zdassets.com/theme_assets/678183/cc59daa07820943e943c2fc283b9079d7003ff76.svg"/></a>

# IL2X (Currently Experimental)
Translates .NET IL to supported and unsuported .NET platforms and architectures. (Powered by <a href="https://github.com/reignstudios/cecil">Mono.Cecil</a>)<br>
NOTE: <a href="https://github.com/reignstudios/CS2X">CS2X</a> will be for GPU targets

## Goals
This projects focus is on translating .NET IL for non-supported .NET targets & performance. Portibility is a big focus.
* Native C performance
* C89: modern, legacy and embedded platforms (x86, x64, MIPS, SPARK, RISC-V, PPC, 68k, AVR, etc)
* CC65: 6502 platforms (Atari, C64, NES, Apple II, etc)
* SDCC: Many targets (ColecoVision, etc)
* z88dk: Z80 platforms
* Assembly: CP1610 (Intellivision)
* Retarget: Custom assembly targets (FPGA CPU, 16bit bytes, etc)
* Custom Standard lib(s) for various targets.
* Other lang targets: Java, JS, ActionScript, etc (portability or framework targeting)
* Documentation

## Project libraries
* IL2X.Core: .NET IL compiler/emitter lib
* IL2X.CLI: CLI interface for IL2X.Core
* IL2X.CoreLib: The IL2X CoreLib & runtime base

## What IL2X aims to provide CoreCLR / .NET-Native / Mono / Mono-AOT doesn't
* True portability. IL2X output will compile to C89 along with a CPU agnostic GC, Boehm or low-ram embedded GC. Special or platform specific C targets will have options. Such as storing string literals in ROM, VC++, Clang or GCC specifics etc.
* Statically compile the entire program & dependencies in a single AOT binary from any C compiler.
* IL2X can be faster than all currently available .NET runtimes when it comes to heavy number crunching thanks to mature C optimizers and lighter weight code gen.
* Allows you to directly invoke C methods statically for better optimizations vs using DllImport etc.
* Supports many C compilers allowing you to choose what's best.

## Building
NOTE: To clone repo you will need the <a href=https://git-lfs.github.com>Git Large File Storage</a></a>

* Prerequisites
	* VS 2022, vscode, Rider, etc
	* .NET 8

## Is this project ready for general use?
No still experimental.
