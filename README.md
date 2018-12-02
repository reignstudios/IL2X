# IL2X (Currently Experimental)
Translates .NET IL to non .NET languages and runtimes. (Powered by Mono.Cecil)<br>
If you're looking for CS2X: https://github.com/reignstudios/CS2X

## Goals
This project will focus on translating .NET IL for non-supported .NET targets.
* IL2C: C89 [legacy, embedded, obscure C compilers]
* IL2CPP: C++ [embedded, modern, portable and readable]
* IL2JV: Java [Android or other]
* IL2JS: javaScript [native fast loading browser apps/widets]
* IL2TS: TypeScript [interface wrapper for JS if useful]
* IL2PY2: Python2 [if useful]
* IL2PY3: Python3 [if useful]
* IL2AS: ActionScript (Flash) [if useful]
* IL2GO: Go [if useful]
* IL2D: D [if useful]
* IL2N: Nim [if useful]

## Project libraries
* IL2X.Core: .NET IL translator/compiler lib
* IL2X.CLI: CLI interface for IL2X.Core