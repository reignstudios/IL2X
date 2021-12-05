# <a href="https://discord.gg/QmJEGER9An"><image src="https://theme.zdassets.com/theme_assets/678183/cc59daa07820943e943c2fc283b9079d7003ff76.svg"/></a>

# IL2X (Currently Experimental)
Translates .NET IL to supported and unsuported .NET platforms and architectures. (Powered by <a href="https://github.com/reignstudios/cecil">Mono.Cecil</a>)<br><br>
<b>If IL2X works well, CS2X will focus on shader langs mostly:</b> https://github.com/reignstudios/CS2X

## Goals
This projects focus is on translating .NET IL for non-supported .NET targets & performance. Portibility is a big focus.
* Native C performance
* C89: modern, legacy and embedded platforms (x86, x64, MIPS, SPARK, RISC-V, PPC, 68k, AVR, etc)
* CC65: 6502 platforms (Atari, C64, NES, Apple II, etc)
* SDCC: Many targets (ColecoVision, etc)
* Assembly: CP1610 (Intellivision)
* Retarget: Custom assembly targets (FPGA CPU, 16bit bytes, etc)
* Custom Standard lib(s) for various targets.
* Documentation

## Project libraries
* IL2X.Core: .NET IL compiler/emitter lib
* IL2X.CLI: CLI interface for IL2X.Core
* IL2X.CoreLib: The IL2X CoreLib & runtime base

## What IL2X arms to provide CoreCLR / .NET-Native / Mono / Mono-AOT doesn't
* True portability. IL2X output should compile without requiring explicit support for different target platforms as portable C does much of this already along with portable GC's strantagies that can compile to any OS or embedded platform. There are special case exceptions and IL2X will try to take care of those. Such as storing string literals in ROM etc.
* Statically compile the entire programs dependencies (this is required as IL2X can be thought of as a AOT-JIT => C89 etc).
* IL2X can be faster than all currently available .NET runtimes when it comes to heavy number crunching thanks to mature C optimizers and lighter weight code gen.
* Allows you to directly invoke C methods statically for better optimizations vs using DllImport.
* Supports many C compilers allowing you to choose what's best.

## Is this project ready for general use?
No still experimental.