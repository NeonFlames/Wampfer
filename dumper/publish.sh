#!/bin/bash
dotnet publish -c Release /p:DebugType=None /p:DebugSymbols=false --os $1 -a $2
