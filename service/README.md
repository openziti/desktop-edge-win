Building Windows Tunneler
-------------------------

You'll need
===========
* CMake - in `$PATH` or in standard location
* migw - `cgo` requires gcc/mingw
* ninja 
* bash

Setup
=====
checkout github.com/netfoundry/ziti-tunneler-sdk-c somewhere
```shell script
git clone --recurse-submodules github.com/netfoundry/ziti-tunneler-sdk-c
```

Build
===== 
```shell script
$ export TUNNELER_SDK_DIR=[your checkout location from above]
$ ./build.sh
```

Running
=======
`ziti-wintun.exe` requires `ziti.dll` and `uv.dll`. Build script copies them to the same directory as executable.
If you copy/move executable don't forget to copy/move those DLLs.    