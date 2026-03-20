/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace VerifyingFiles.PInvoke {
    public static class Win32Crypto {
        //based on https://docs.microsoft.com/en-us/archive/blogs/alejacma/how-to-get-information-from-a-crl-net

        #region APIs
        [DllImport("CRYPT32.DLL", EntryPoint = "CryptQueryObject", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern Boolean CryptQueryObject(
            Int32 dwObjectType,
            [MarshalAs(UnmanagedType.LPWStr)] String pvObject,
            Int32 dwExpectedContentTypeFlags,
            Int32 dwExpectedFormatTypeFlags,
            Int32 dwFlags,
            IntPtr pdwMsgAndCertEncodingType,
            IntPtr pdwContentType,
            IntPtr pdwFormatType,
            IntPtr phCertStore,
            IntPtr phMsg,
            ref IntPtr ppvContext
            );
        [DllImport("CRYPT32.DLL", EntryPoint = "CryptQueryObject", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern Boolean CryptQueryObject2(
            Int32 dwObjectType,
            IntPtr pvObject,
            Int32 dwExpectedContentTypeFlags,
            Int32 dwExpectedFormatTypeFlags,
            Int32 dwFlags,
            IntPtr pdwMsgAndCertEncodingType,
            IntPtr pdwContentType,
            IntPtr pdwFormatType,
            ref IntPtr phCertStore,
            IntPtr phMsg,
            ref IntPtr ppvContext
        );

        [DllImport("CRYPT32.DLL", EntryPoint = "CertFreeCRLContext", SetLastError = true)]
        public static extern Boolean CertFreeCRLContext(
            IntPtr pCrlContext
        );

        [DllImport("CRYPT32.DLL", EntryPoint = "CertNameToStr", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern Int32 CertNameToStr(
            Int32 dwCertEncodingType,
            ref CRYPTOAPI_BLOB pName,
            Int32 dwStrType,
            StringBuilder psz,
            Int32 csz
        );

        [DllImport("CRYPT32.DLL", EntryPoint = "CertFindExtension", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CertFindExtension(
            [MarshalAs(UnmanagedType.LPStr)] String pszObjId,
            Int32 cExtensions,
            IntPtr rgExtensions
        );

        [DllImport("CRYPT32.DLL", EntryPoint = "CryptFormatObject", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern Boolean CryptFormatObject(
            Int32 dwCertEncodingType,
            Int32 dwFormatType,
            Int32 dwFormatStrType,
            IntPtr pFormatStruct,
            [MarshalAs(UnmanagedType.LPStr)] String lpszStructType,
            IntPtr pbEncoded,
            Int32 cbEncoded,
            StringBuilder pbFormat,
            ref Int32 pcbFormat
        );

        #endregion APIs

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        public struct CRL_CONTEXT {
            public Int32 dwCertEncodingType;
            public IntPtr pbCrlEncoded;
            public Int32 cbCrlEncoded;
            public IntPtr pCrlInfo;
            public IntPtr hCertStore;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CRL_INFO {
            public Int32 dwVersion;
            public CRYPT_ALGORITHM_IDENTIFIER SignatureAlgorithm;
            public CRYPTOAPI_BLOB Issuer;
            public FILETIME ThisUpdate;
            public FILETIME NextUpdate;
            public Int32 cCRLEntry;
            public IntPtr rgCRLEntry;
            public Int32 cExtension;
            public IntPtr rgExtension;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CRYPT_ALGORITHM_IDENTIFIER {
            [MarshalAs(UnmanagedType.LPStr)] public String pszObjId;
            public CRYPTOAPI_BLOB Parameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CRYPTOAPI_BLOB {
            public Int32 cbData;
            public IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME {
            public Int32 dwLowDateTime;
            public Int32 dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CRL_ENTRY {
            public CRYPTOAPI_BLOB SerialNumber;
            public FILETIME RevocationDate;
            public Int32 cExtension;
            public IntPtr rgExtension;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CERT_EXTENSION {
            [MarshalAs(UnmanagedType.LPStr)] public String pszObjId;
            public Boolean fCritical;
            public CRYPTOAPI_BLOB Value;
        }

        #endregion Structs

        #region Consts

        public const Int32 CERT_QUERY_OBJECT_BLOB = 0x00000002;
        public const Int32 CERT_QUERY_CONTENT_CRL = 3;
        public const Int32 CERT_QUERY_CONTENT_FLAG_CRL = 1 << CERT_QUERY_CONTENT_CRL;
        public const Int32 CERT_QUERY_FORMAT_BINARY = 1;
        public const Int32 CERT_QUERY_FORMAT_BASE64_ENCODED = 2;
        public const Int32 CERT_QUERY_FORMAT_ASN_ASCII_HEX_ENCODED = 3;
        public const Int32 CERT_QUERY_FORMAT_FLAG_BINARY = 1 << CERT_QUERY_FORMAT_BINARY;
        public const Int32 CERT_QUERY_FORMAT_FLAG_BASE64_ENCODED = 1 << CERT_QUERY_FORMAT_BASE64_ENCODED;
        public const Int32 CERT_QUERY_FORMAT_FLAG_ASN_ASCII_HEX_ENCODED = 1 << CERT_QUERY_FORMAT_ASN_ASCII_HEX_ENCODED;
        public const Int32 CERT_QUERY_FORMAT_FLAG_ALL = CERT_QUERY_FORMAT_FLAG_BINARY | CERT_QUERY_FORMAT_FLAG_BASE64_ENCODED | CERT_QUERY_FORMAT_FLAG_ASN_ASCII_HEX_ENCODED;

        public const Int32 X509_ASN_ENCODING = 0x00000001;
        public const Int32 PKCS_7_ASN_ENCODING = 0x00010000;

        public const Int32 X509_NAME = 7;

        public const Int32 CERT_SIMPLE_NAME_STR = 1;
        public const Int32 CERT_OID_NAME_STR = 2;
        public const Int32 CERT_X500_NAME_STR = 3;

        public const String szOID_CRL_REASON_CODE = "2.5.29.21";

        #endregion

        private static List<string> GetRevokedSerialNumbers(CRL_INFO stCrlInfo) {
            List<string> rtn = new List<string>();
            var rgCrlEntry = stCrlInfo.rgCRLEntry;

            for (var i = 0; i < stCrlInfo.cCRLEntry; i++) {
                var serial = string.Empty;
                var stCrlEntry = (CRL_ENTRY)Marshal.PtrToStructure(rgCrlEntry, typeof(CRL_ENTRY));

                IntPtr pByte = stCrlEntry.SerialNumber.pbData;
                for (var j = 0; j < stCrlEntry.SerialNumber.cbData; j++) {
                    Byte bByte = Marshal.ReadByte(pByte);
                    serial = bByte.ToString("X").PadLeft(2, '0') + serial;
                    pByte = pByte + Marshal.SizeOf(typeof(byte));

                }
                rtn.Add(serial);
                rgCrlEntry = rgCrlEntry + Marshal.SizeOf(typeof(CRL_ENTRY));
            }

            return rtn;
        }

        public static DateTime FileTimeToDateTime(FILETIME fileTime) {
            DateTime dateTime;
            IntPtr int64Ptr = Marshal.AllocHGlobal(sizeof(Int64));
            try {
                Marshal.StructureToPtr(fileTime, int64Ptr, true);
                dateTime = DateTime.FromFileTime(Marshal.ReadInt64(int64Ptr));
            } finally {
                Marshal.FreeHGlobal(int64Ptr);
            }

            return dateTime;
        }

        public static CrlInfo FromBlob(byte[] CrlDERBytes) {

            var phCertStore = IntPtr.Zero;
            var pvContext = IntPtr.Zero;
            var hCrlData = new GCHandle();
            var hCryptBlob = new GCHandle();
            try {
                hCrlData = GCHandle.Alloc(CrlDERBytes, GCHandleType.Pinned);
                CRYPTOAPI_BLOB stCryptBlob;
                stCryptBlob.cbData = CrlDERBytes.Length;
                stCryptBlob.pbData = hCrlData.AddrOfPinnedObject();
                hCryptBlob = GCHandle.Alloc(stCryptBlob, GCHandleType.Pinned);

                if (!CryptQueryObject2(
                    CERT_QUERY_OBJECT_BLOB,
                    hCryptBlob.AddrOfPinnedObject(),
                    CERT_QUERY_CONTENT_FLAG_CRL,
                    CERT_QUERY_FORMAT_FLAG_BINARY,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    ref phCertStore,
                    IntPtr.Zero,
                    ref pvContext
                )) {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CRL is Corrupted.");
                }
                var stCrlContext = (CRL_CONTEXT)Marshal.PtrToStructure(pvContext, typeof(CRL_CONTEXT));
                var stCrlInfo = (CRL_INFO)Marshal.PtrToStructure(stCrlContext.pCrlInfo, typeof(CRL_INFO));

                CrlInfo info = new CrlInfo();
                info.validTo = FileTimeToDateTime(stCrlInfo.NextUpdate);
                info.validFrom = FileTimeToDateTime(stCrlInfo.ThisUpdate);
                info.RevokedSerialNumbers = GetRevokedSerialNumbers(stCrlInfo);

                return info;
            } finally {
                if (hCrlData.IsAllocated) hCrlData.Free();
                if (hCryptBlob.IsAllocated) hCryptBlob.Free();
                if (!pvContext.Equals(IntPtr.Zero)) {
                    CertFreeCRLContext(pvContext);
                }
            }
        }

        public struct CrlInfo {
            public DateTime validTo;
            public DateTime validFrom;
            public List<string> RevokedSerialNumbers;
        }
    }
}
