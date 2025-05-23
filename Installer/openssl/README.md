# OpenSSL and FIPS

The openssl.exe binary provided here is necessry when deploying a Ziti Desktop Edge for Windows with
FIPS mode enabled. The openssl.exe binary will be used to generate a `fipsmodule.cnf` during install
time.

This file must be generated on every system in order to be compliant by running the command:
```
openssl fipsinstall -out /path/to/fipsmodule.cnf -module /path/to/fips.dll
```

The fips.dll supplied here is for use with the Ziti Desktop Edge for Windows only. The precise version
of the fips.dll can be determined by looking at the properties of the dll from the Windows explorer.

## Building the FIPS module

The `fips.dll` provided here was built by strictly following the security policy document specified
by the [OpenSSL source guidance found here](https://openssl-library.org/source/).