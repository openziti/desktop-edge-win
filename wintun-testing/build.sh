#!/usr/bin/env bash
die() {
  echo "Error: $1"
  exit 1
}

[ -z "$TUNNELER_SDK_DIR" ] && die 'set TUNNELER_SDK_DIR'

if [[ $(type -P cmake) ]]; then
  CMAKE=cmake
elif [ -x "/c/Program Files/Cmake/bin/cmake.exe" ]; then
  echo "cmake found in standard location"
  CMAKE='/c/Program Files/Cmake/bin/cmake.exe'
fi

mkdir build
mkdir install

if [[ ! -f ./build/CMakeCache.txt ]]; then
   "${CMAKE}" -G Ninja -S ${TUNNELER_SDK_DIR} -B ./build -DCMAKE_INSTALL_PREFIX=./install
fi
"${CMAKE}" --build ./build --target install

cp ./install/lib/ziti.dll .
cp ./install/lib/libuv.dll .

export CGO_CFLAGS="-DNOGDI -I $(cygpath -a -m ./install/include)"
export CGO_LDFLAGS="-L $(cygpath -a -m ./install/lib)"
go build -a ./ziti-wintun

