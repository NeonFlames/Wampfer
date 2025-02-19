#!/bin/bash
dotnet publish -c Release /p:DebugType=None /p:DebugSymbols=false /p:PublishTrimmed=true --os $1 -a $2
