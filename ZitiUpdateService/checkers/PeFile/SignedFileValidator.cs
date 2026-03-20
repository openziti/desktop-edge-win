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
using System.IO;
using System.Net.Http;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NLog;
using VerifyingFiles.PInvoke;

namespace ZitiUpdateService.Checkers.PeFile {
    public class SignedFileValidator {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public const int LengthOfChecksum = 4; //the checksum is 4 bytes
        public const int LengthCertificateTable = 8; //the Certificate Table is 8 bytes

        public HashPositions ImportantHashPositions { get; private set; }
        public string FilePath { get; private set; }
        public PeType Type { get; private set; }
        public IMAGE_FILE_HEADER CoffHeader { get; private set; }
        public IMAGE_OPTIONAL_HEADER32 StandardFieldsPe32 { get; private set; }
        public IMAGE_OPTIONAL_HEADER64 StandardFieldsPe32Plus { get; private set; }

        private readonly X509Certificate2 expectedRootCa = null;
        private readonly X509Store empty = new X509Store();
        private X509Store SystemCAs = new X509Store();
        private const string oldKnownThumbprint = "39636E9F5E80308DE370C914CE8112876ECF4E0C";

        public SignedFileValidator(string pathToFile) {
            X509Store store = new X509Store("MY", StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            SystemCAs.Open(OpenFlags.ReadWrite);
            SystemCAs.AddRange(store.Certificates);

            expectedRootCa = certFromResource("ZitiUpdateService.checkers.PeFile.openziti.rootCA.rsa.pem.txt");
            FilePath = pathToFile;
            ImportantHashPositions = new HashPositions();

            parse();
            if (ImportantHashPositions.reorderNeeded) {
                ImportantHashPositions.SectionTableHeaders.Sort((x, y) => x.PointerToRawData.CompareTo(y.PointerToRawData));
            }
        }

        private X509Certificate2 certFromResource(string resourceName) {
            var assembly = this.GetType().Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (BinaryReader reader = new BinaryReader(stream)) {
                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return new X509Certificate2(bytes);
            }
        }

        // ReSharper disable once InconsistentNaming
        private void parse() {
            using (FileStream stream = new FileStream(FilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream)) {

                //seek to 0x3c to get right to the PE header location...
                stream.Seek(0x3c, SeekOrigin.Begin);
                uint startCoffHeader = BitConverter.ToUInt32(reader.ReadBytes(4), 0);
                ImportantHashPositions.SetStartCoffHeader(startCoffHeader);
                stream.Seek(startCoffHeader, SeekOrigin.Begin);

                //parse COFF Header (28 or 24 bytes): https://docs.microsoft.com/en-us/windows/win32/debug/pe-format#coff-file-header-object-and-image
                byte[] coffHeaderBytes = reader.ReadBytes(24);
                CoffHeader = StructHelper.FromBytes<IMAGE_FILE_HEADER>(coffHeaderBytes);

                byte[] magicNumberBytes = reader.ReadBytes(2);
                int magicNumber = BitConverter.ToInt16(magicNumberBytes, 0);
                Type = magicNumber == 0x10b ? PeType.Pe32 : PeType.Pe32Plus;
                stream.Seek(-2, SeekOrigin.Current); //skip back to before the magic number so the struct below is created properly

                //parse Optional Header Standard and Windows-Specific fields (66 or 88 bytes)
                //see: https://docs.microsoft.com/en-us/windows/win32/debug/pe-format#optional-header-standard-fields-image-only
                if (Type == PeType.Pe32) {
                    byte[] headerBytes = reader.ReadBytes(Marshal.SizeOf(typeof(IMAGE_OPTIONAL_HEADER32)));
                    StandardFieldsPe32 = StructHelper.FromBytes<IMAGE_OPTIONAL_HEADER32>(headerBytes);
                } else {
                    byte[] headerBytes = reader.ReadBytes(Marshal.SizeOf(typeof(IMAGE_OPTIONAL_HEADER64)));
                    StandardFieldsPe32Plus = StructHelper.FromBytes<IMAGE_OPTIONAL_HEADER64>(headerBytes);
                }

                ImageOptionalHeaderWrapper wrapper = new ImageOptionalHeaderWrapper() {
                    h32 = StandardFieldsPe32,
                    h64 = StandardFieldsPe32Plus,
                    IsPe = Type == PeType.Pe32
                };
                ImportantHashPositions.SetCertificateDetails(Type, wrapper.SizeOfHeaders, wrapper.CertificateTableVirtualAddress, wrapper.CertificateTableSize);

                for (int i = 0; i < CoffHeader.NumberOfSections; i++) {
                    byte[] nextSection = reader.ReadBytes(Marshal.SizeOf(typeof(IMAGE_SECTION_HEADER))); //should be 40 bytes...
                    IMAGE_SECTION_HEADER sectionTableHeader = StructHelper.FromBytes<IMAGE_SECTION_HEADER>(nextSection);
                    ImportantHashPositions.AddSectionTableHeader(sectionTableHeader);
                }
            }
        }

        private void addCertToList(SignerInfo si, List<X509Certificate2> list) {

            X509Certificate2 cert = si.Certificate;
            if (list.Find(x => x.Thumbprint == cert.Thumbprint) == null) {
                list.Add(cert);
            } else {
                Logger.Debug("Certificate with Thumbprint {0} already in list. skipping.", cert.Thumbprint);
            }
        }

        public List<X509Certificate2> ExtractVerifiedSignatureCertificates() {
            List<X509Certificate2> list = new List<X509Certificate2>();
            using (FileStream stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream)) {
                //verify all the embedded signatures
                stream.Seek(this.ImportantHashPositions.CertificateTableStart, SeekOrigin.Begin);
                int readCertLen = reader.ReadInt32();
                int readRevision = reader.ReadInt16();
                int readCertType = reader.ReadInt16();
                byte[] pkcs7 = reader.ReadBytes(readCertLen);

                SignedCms cms = new SignedCms();
                cms.Decode(pkcs7);
                cms.CheckHash();

                //shout out to Scott MG: https://social.msdn.microsoft.com/Forums/windowsdesktop/en-US/655f5c27-b049-4275-a8b3-cc1c0be2b4f2/retrieve-certificate-info-for-dualsigned-sha1sha256
                //for indicating that the unsigned attributes contain the other SignerInfos
                foreach (var cmsSi in cms.SignerInfos) {
                    cmsSi.CheckSignature(true);
                    if (VerifyTrust(SystemCAs, expectedRootCa, cmsSi.Certificate).Result) {
                        VerifyFileHash(cms, cmsSi);
                        addCertToList(cmsSi, list);
                    }
                    if (cmsSi.UnsignedAttributes.Count > 0) {
                        foreach (var unsignedAttr in cmsSi.UnsignedAttributes) {
                            foreach (AsnEncodedData asn in unsignedAttr.Values) {
                                SignedCms innerCms = new SignedCms();
                                innerCms.Decode(asn.RawData);
                                innerCms.CheckHash();
                                if (innerCms.SignerInfos.Count > 0) {
                                    SignerInfo innerSignerInfo = innerCms.SignerInfos[0];
                                    try {
                                        innerSignerInfo.CheckSignature(false);
                                    } catch (CryptographicException) {
                                        if (innerSignerInfo.Certificate.Thumbprint == oldKnownThumbprint) {
                                            //special handling for the known 'old' NetFoundry signing certificate, now expired...
                                            //TODO: remove this code after 2021
                                            //just allow this one error
                                            Logger.Warn("Ignoring timestamp validity issue for the existing code signing certificate. Subject: {0}, Thumbprint: {1}", innerSignerInfo.Certificate.Subject, innerSignerInfo.Certificate.Thumbprint);
                                        } else {
                                            if (IsCertificateOpenZitiVerifies(innerSignerInfo.Certificate)) {
                                                Logger.Debug("Certificate {} from {} is not one needed to be verified", innerSignerInfo.Certificate.SubjectName, innerSignerInfo.Certificate.Issuer);
                                            } else {
                                                throw;
                                            }
                                        }
                                    }

                                    if (VerifyTrust(SystemCAs, expectedRootCa, innerSignerInfo.Certificate).Result) {
                                        VerifyFileHash(innerCms, innerSignerInfo);
                                        addCertToList(innerSignerInfo, list);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return list;
        }

        public bool IsCertificateOpenZitiVerifies(X509Certificate2 cert) {
            bool yesOrNo = cert.Subject.ToLower().Contains("netfoundry") || cert.Subject.ToLower().Contains("openziti");
            if (yesOrNo) {
                Logger.Debug("Certificate does     need verification: {0}", cert.Subject);
            } else {
                Logger.Debug("Certificate does not need verification: {0}", cert.Subject);
            }
            return yesOrNo;
        }

        public void Verify() {
            Logger.Debug("extracting certificates from file: {0}", FilePath);
            List<X509Certificate2> list = ExtractVerifiedSignatureCertificates();
            Logger.Info("Certificates extracted: {0}", list.Count);
            foreach (X509Certificate2 cert in list) {
                Logger.Info("Checking certificate: [{0}] {1}", cert.Thumbprint, cert.Subject);
                if (IsCertificateOpenZitiVerifies(cert)) {
                    //verify this certificate was issued from the known CA
                    try {
                        Logger.Info("Verifying trust of certificate: [{0}] {1}", cert.Subject, cert.Thumbprint);
                        VerifyTrust(empty, expectedRootCa, cert, true).Wait();
                        Logger.Info("Download verification complete. Certificate [{0}] {1} was verified signed by [{2}] {3}", cert.Thumbprint, cert.Subject, expectedRootCa.Thumbprint, expectedRootCa.Subject);
                        return; //yes!
                    } catch (Exception e) {
                        Logger.Debug("Could not verify certificate. Exception encountered: {}", e);
                    }
                } else {
                    Logger.Debug("Certificate {} from {} is not one needed to be verified", cert.SubjectName, cert.Issuer);
                }
            }
            Logger.Debug("verify did not succeed. throwing exception");
            throw new CryptographicException("Executable not signed by an appropriate certificate");
        }

        public void VerifyFileHash(SignedCms cms, SignerInfo signerInfo) {
            if (!IsCertificateOpenZitiVerifies(signerInfo.Certificate)) {
                return;
            }

            //calculate the hash using the first signed info
            var hashAlg = IncrementalHash.CreateHash(new HashAlgorithmName(signerInfo.DigestAlgorithm.FriendlyName.ToUpper()));
            byte[] calculatedHash = CalculatePeHashStreaming(hashAlg);
            string hash = BitConverter.ToString(calculatedHash).Replace("-", "");

            string content = BitConverter.ToString(cms.ContentInfo.Content).Replace("-", "");
            if (!content.Contains(hash)) {
                throw new CryptographicException("The expected hash and the actual hash did not match!");
            }
        }

        public async static Task<bool> VerifyTrust(X509Store cas, X509Certificate2 trustedRootCertificateAuthority, X509Certificate2 certificate) {
            return await VerifyTrust(cas, trustedRootCertificateAuthority, certificate, false);
        }

        public async static Task<bool> VerifyTrust(X509Store cas, X509Certificate2 trustedRootCertificateAuthority, X509Certificate2 certificate, bool verifyTrustedRoot) {
            X509Chain chain = new X509Chain();
            chain.ChainPolicy.ExtraStore.Add(trustedRootCertificateAuthority);
            chain.ChainPolicy.ExtraStore.AddRange(cas.Certificates);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(new X509Certificate2(certificate));
            List<Exception> exceptions = new List<Exception>();
            foreach (X509ChainStatus status in chain.ChainStatus) {
                if (status.Status == X509ChainStatusFlags.NoError || status.Status == X509ChainStatusFlags.UntrustedRoot) {
                    //X509ChainStatusFlags.UntrustedRoot simply means it was not found in the computer/user's trust store.... that's fine...
                } else {
                    if (status.Status == X509ChainStatusFlags.NotTimeValid && certificate.Thumbprint == oldKnownThumbprint) {
                        //X509ChainStatusFlags.NotTimeValid means the certificate has expired - we're allowing this for now (summer 2021) to allow older clients to update
                        Logger.Warn("Executable is signed using the old signing certificate. Allowing this certificate to report as expired");
                    } else {
                        exceptions.Add(new CryptographicException("Could not verify trust: " + status.StatusInformation));
                    }
                }
            }

            //final check - make sure the last certificate matches the expected thumbprint
            if (verifyTrustedRoot && chain.ChainElements[chain.ChainElements.Count - 1].Certificate.Thumbprint != trustedRootCertificateAuthority.Thumbprint) {
                exceptions.Add(new Exception("Could not verify trust. The expected thumbprint was not found!"));
            }

            foreach (var e in certificate.Extensions) {
                if (e.Oid.Value == "2.5.29.31") { //2.5.29.31 == CRL Distribution Points
                    try {
                        CrlDistributionPointParser t = new CrlDistributionPointParser(e.RawData);

                        if (t != null) {
                            foreach (string url in t.urls) {
                                try {
                                    //good - things are going the way we want... keep going
                                    //fetch the CRL from the url provided and parse it...
                                    byte[] crlBytes = await new HttpClient().GetByteArrayAsync(url);

                                    if (crlBytes != null && crlBytes.Length > 0) {
                                        //fetch the crl
                                        Win32Crypto.CrlInfo info = Win32Crypto.FromBlob(crlBytes);
                                        if (info.RevokedSerialNumbers.Contains(certificate.SerialNumber)) {
                                            exceptions.Add(new CryptographicException("Serial number " + certificate.SerialNumber + " has been revoked."));
                                        }
                                    } else {
                                        exceptions.Add(new CryptographicException("Could not retrieve revocation list from " + url + "- cannot verify trust"));
                                    }
                                } catch (Exception innerException) {
                                    exceptions.Add(new CryptographicException("crl at " + url + " could not be used", innerException));
                                }
                            }
                        }
                    } catch (Exception ex) {
                        exceptions.Add(ex);
                    }
                }
            }

            if (exceptions.Count > 0) {
                throw new AggregateException(exceptions);
            }

            return true;
        }

        public byte[] CalculatePeHashStreaming(IncrementalHash alg) {
            using (FileStream stream = new FileStream(FilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream)) {
                stream.Seek(ImportantHashPositions.Chunk1.Start, SeekOrigin.Begin);
                alg.AppendData(reader.ReadBytes(ImportantHashPositions.Chunk1.Length));

                stream.Seek(ImportantHashPositions.Chunk2.Start, SeekOrigin.Begin);
                alg.AppendData(reader.ReadBytes(ImportantHashPositions.Chunk2.Length));

                stream.Seek(ImportantHashPositions.Chunk3.Start, SeekOrigin.Begin);
                alg.AppendData(reader.ReadBytes(ImportantHashPositions.Chunk3.Length));

                int blockSize = 8192;
                int read = 0;
                byte[] buf = new byte[blockSize];

                foreach (IMAGE_SECTION_HEADER h in ImportantHashPositions.SectionTableHeaders) {
                    stream.Seek(h.PointerToRawData, SeekOrigin.Begin);
                    if (h.SizeOfRawData <= blockSize) {
                        //just read the full block as one
                        alg.AppendData(reader.ReadBytes((int)h.SizeOfRawData));
                    } else {
                        long pos = stream.Position;
                        long end = h.PointerToRawData + h.SizeOfRawData - blockSize;
                        //process the data into blocks
                        while (pos < end) {
                            read = stream.Read(buf, 0, blockSize);
                            alg.AppendData(buf, 0, read);
                            pos += blockSize;
                        }

                        int remaining = (int)(end - pos + blockSize);
                        if (remaining > 0) {
                            read = stream.Read(buf, 0, remaining);
                            alg.AppendData(buf, 0, read);
                        }
                    }
                }

                /*
                14. Create a value called FILE_SIZE, which is not part of the signature. Set this value to the 
                    image’s file size, acquired from the underlying file system. If FILE_SIZE is greater than 
                    SUM_OF_BYTES_HASHED, the file contains extra data that must be added to the hash. This data 
                    begins at the SUM_OF_BYTES_HASHED file offset, and its length is:
                         (File Size) – ((Size of AttributeCertificateTable) + SUM_OF_BYTES_HASHED)
                    Note: The size of Attribute Certificate Table is specified in the second ULONG value in the 
                    Certificate Table entry (32 bit: offset 132, 64 bit: offset 148) in Optional Header Data 
                    Directories.
                 */
                long FILE_SIZE = new FileInfo(FilePath).Length;

                long leftoverBytesToHash = FILE_SIZE - ImportantHashPositions.SumOfBytesHashed - ImportantHashPositions.CertificateTableSize;

                if (leftoverBytesToHash > 0) {
                    //HashBlock(alg, reader, SUM_OF_BYTES_HASHED + certTableSize, FILE_SIZE);
                    stream.Seek(ImportantHashPositions.SumOfBytesHashed, SeekOrigin.Begin);
                    byte[] leftoverBytes = reader.ReadBytes((int)leftoverBytesToHash);
                    alg.AppendData(leftoverBytes);
                } else {
                    //no more bytes to hash...
                }
                //alg.TransformFinalBlock(new byte[0], 0, 0);

                byte[] result = alg.GetHashAndReset();

                return result;
            }
        }

        public class HashPositions {
            internal HashPositions() {
                Chunk1 = new Range();
                Chunk2 = new Range();
                Chunk3 = new Range();
                SectionTableHeaders = new List<IMAGE_SECTION_HEADER>();
            }
            /*
             * These functions are all based from the document linked to from the bottom of:
             *      https://docs.microsoft.com/en-us/windows/win32/debug/pe-format#optional-header-standard-fields-image-only
             *
             * Link to the document:
             *      https://download.microsoft.com/download/9/c/5/9c5b2167-8017-4bae-9fde-d599bac8184a/Authenticode_PE.docx
             *
             * Relevant section copied from that document:
             * To calculate the hash value:
                1.  Load the image header into memory.
                2.  Initialize a hash algorithm context.
                3.  Hash the image header from its base to immediately before the start of the 
                    checksum address, as specified in Optional Header Windows-Specific Fields.
                4.  Skip over the checksum, which is a 4-byte field.
                5.  Hash everything from the end of the checksum field to immediately before the start of 
                    the Certificate Table entry, as specified in Optional Header Data Directories.
                6.  Get the Attribute Certificate Table address and size from the Certificate Table entry. 
                    For details, see section 5.7 of the PE/COFF specification.
                7.  Exclude the Certificate Table entry from the calculation and hash everything from the 
                    end of the Certificate Table entry to the end of image header, including Section Table 
                    (headers).The Certificate Table entry is 8 bytes long, as specified in Optional Header 
                    Data Directories. 
                8.  Create a counter called SUM_OF_BYTES_HASHED, which is not part of the signature. Set 
                    this counter to the SizeOfHeaders field, as specified in Optional Header Windows-Specific 
                    Field.
                9.  Build a temporary table of pointers to all of the section headers in the image. The 
                    NumberOfSections field of COFF File Header indicates how big the table should be. Do not 
                    include any section headers in the table whose SizeOfRawData field is zero. 
                10. Using the PointerToRawData field (offset 20) in the referenced SectionHeader structure as 
                    a key, arrange the table's elements in ascending order. In other words, sort the section 
                    headers in ascending order according to the disk-file offset of the sections.
                11. Walk through the sorted table, load the corresponding section into memory, and hash the entire 
                    section. Use the SizeOfRawData field in the SectionHeader structure to determine the amount 
                    of data to hash.
                12. Add the section’s SizeOfRawData value to SUM_OF_BYTES_HASHED.
                13. Repeat steps 11 and 12 for all of the sections in the sorted table.
                14. Create a value called FILE_SIZE, which is not part of the signature. Set this value to the 
                    image’s file size, acquired from the underlying file system. If FILE_SIZE is greater than 
                    SUM_OF_BYTES_HASHED, the file contains extra data that must be added to the hash. This data 
                    begins at the SUM_OF_BYTES_HASHED file offset, and its length is:
                        (File Size) – ((Size of AttributeCertificateTable) + SUM_OF_BYTES_HASHED)
                    Note: The size of Attribute Certificate Table is specified in the second ULONG value in the 
                    Certificate Table entry (32 bit: offset 132, 64 bit: offset 148) in Optional Header Data 
                    Directories.
                15. Finalize the hash algorithm context.
                    Note: This procedure uses offset values from the PE/COFF specification, version 8.1 . 
                    For authoritative offset values, refer to the most recent version of the PE/COFF specification.
             */

            public List<IMAGE_SECTION_HEADER> SectionTableHeaders { get; private set; }

            /// <summary>
            /// Chunk1 Start will set the position in the file which the hashing of the file should begin from
            /// This is always the start of the file and thus is hardcoded to 0 and takes no parameters
            /// 
            /// Chunk1 End is defined to be: "immediately before the start of the checksum address"
            ///
            /// Relevant step: 
            /// 3. Hash the image header from its base to immediately before the start of the
            ///    checksum address, as specified in Optional Header Windows-Specific Fields.
            /// </summary>
            public Range Chunk1 { get; private set; }

            /// <summary> 
            /// Chunk2 Start is defined to be: "end of the checksum field"
            /// Chunk2 End is defined to be: "immediately before the start of the Certificate Table entry"
            /// 
            /// Relevant step: 
            /// 5. Hash everything from the end of the checksum field to immediately before the start of
            ///    the Certificate Table entry, as specified in Optional Header Data Directories.
            /// </summary>
            public Range Chunk2 { get; private set; }

            /// <summary>
            /// Chunk3 Start is defined to be: "the end of the Certificate Table entry"
            /// Chunk3 End is defined to be: "the end of image header"
            ///     When testing/developing this solution - this was found to be the position specified by
            ///     the SizeOfHeaders field
            /// 
            /// Relevant step: 
            /// 7. Exclude the Certificate Table entry from the calculation and hash everything from the
            ///    end of the Certificate Table entry to the end of image header, including Section Table
            ///    (headers).The Certificate Table entry is 8 bytes long, as specified in Optional Header
            ///    Data Directories.
            /// </summary>
            public Range Chunk3 { get; private set; }

            public uint SizeOfHeaders { get; private set; }
            public uint SumOfBytesHashed { get; private set; }
            public long CertificateTableStart { get; private set; }
            public long CertificateTableSize { get; private set; }

            internal bool reorderNeeded = false;
            private uint lastAddress = 0;
            public void AddSectionTableHeader(IMAGE_SECTION_HEADER sectionTableHeader) {

                if (sectionTableHeader.SizeOfRawData > 0) {
                    SectionTableHeaders.Add(sectionTableHeader);
                }

                if (lastAddress < sectionTableHeader.PointerToRawData) {
                    //good - it's in order...
                } else {
                    reorderNeeded = true;
                }
                lastAddress = sectionTableHeader.PointerToRawData;
                SumOfBytesHashed += sectionTableHeader.SizeOfRawData;
            }

            public void SetStartCoffHeader(uint peHeaderStart) {
                Chunk1.Start = 0;
                uint checksumStart = peHeaderStart + 88; //88 is the size of the COFF Header (24) + Standard Headers + Windows-Specific Fields up to checksum (offset 64)
                Chunk1.End = checksumStart;
                Chunk2.Start = checksumStart + LengthOfChecksum;

                //and the four tables before the Certificate Table
                //(ExportTable, ImportTable, ResourceTable, ExceptionTable)
                uint certHeaderTableOffset = checksumStart + 64;

                Chunk2.End = certHeaderTableOffset;
                Chunk3.Start = certHeaderTableOffset + LengthCertificateTable;
            }

            public void SetCertificateDetails(PeType type, uint sizeOfHeaders, uint certificateTableStart,
                uint certificateTableSize) {
                if (type == PeType.Pe32Plus) {
                    uint winSpecificExtraBytes = 16; //need to account for the extra bytes in the Windows-Specific Fields (each field has 4 extra bytes)
                    Chunk2.End += winSpecificExtraBytes;
                    Chunk3.Start += winSpecificExtraBytes;
                }

                Chunk3.End = sizeOfHeaders;
                SizeOfHeaders = sizeOfHeaders;
                SumOfBytesHashed = sizeOfHeaders;
                CertificateTableStart = certificateTableStart;
                CertificateTableSize = certificateTableSize;
            }
        }

        public class Range {
            public long Start { get; internal set; }
            public long End { get; internal set; }

            public int Length => (int)(End - Start);
        }
    }

    public enum PeType {
        Pe32,
        Pe32Plus
    }

    internal class CrlDistributionPointParser {
        internal int offset;
        internal int length;
        internal List<CrlDistributionPointParser> tags = new List<CrlDistributionPointParser>();
        private CrlDistributionPointParser parent = null;
        private AsnTagType type = AsnTagType.NONE;
        internal List<string> urls = new List<string>();
        private int consumedBytes = 0;

        internal CrlDistributionPointParser(byte[] asn1Data) : this(new MemoryStream(asn1Data)) {

        }

        internal CrlDistributionPointParser(MemoryStream asn1Data) : this(null, asn1Data/*new BinaryReader(asn1Data)*/, 0) {

        }

        internal CrlDistributionPointParser(CrlDistributionPointParser parent, MemoryStream memoryStream, /*BinaryReader asn1Reader,*/ int offset) {
            BinaryReader asn1Reader = new BinaryReader(memoryStream);
            this.offset = offset;
            type = readTag(asn1Reader);
            length = readTagLen(asn1Reader);
            this.parent = parent;
            int headerBytes = consumedBytes;
            while (this.consumedBytes < length + headerBytes) {
                switch (type) {
                    case AsnTagType.SEQUENCE:
                    case AsnTagType.ARRAY_ELEMENT:
                        var t = new CrlDistributionPointParser(this, memoryStream, (int)memoryStream.Position);
                        consumedBytes += t.consumedBytes;
                        tags.Add(t);
                        this.urls.AddRange(t.urls);
                        break;
                    case AsnTagType.STRING:
                        consumedBytes += length;
                        urls.Add(System.Text.Encoding.UTF8.GetString(asn1Reader.ReadBytes(length)));
                        break;
                }
            }
        }

        internal AsnTagType readTag(BinaryReader asn1Data) {
            byte tagByte = asn1Data.ReadByte();
            if (tagByte != 0x30 && tagByte != 0xA0 && tagByte != 0x86) {
                throw new CryptographicException("Could not parse CRL Distribution Points. First byte not asn SEQUENCE|ARRAY|STRING marker");
            }

            consumedBytes++;
            if (tagByte == 0x30) return AsnTagType.SEQUENCE;
            if (tagByte == 0xA0) return AsnTagType.ARRAY_ELEMENT;
            if (tagByte == 0x86) return AsnTagType.STRING;
            return AsnTagType.NONE;
        }

        internal int readTagLen(BinaryReader asn1Data) {
            int tagLen = asn1Data.ReadByte();
            consumedBytes++;
            if (tagLen > 128) {
                //means there's more bytes to read that control the overall length of the next section
                int moreBytes = tagLen - 128;
                byte[] lenBytes = new byte[4];
                byte[] d = asn1Data.ReadBytes(moreBytes);

                int pos = 0;
                for (int i = d.Length - 1; i >= 0; i--) {
                    lenBytes[pos++] = d[i];
                }

                tagLen = BitConverter.ToInt32(lenBytes, 0);
                consumedBytes += moreBytes;
            }

            return tagLen;
        }
    }

    internal enum AsnTagType {
        SEQUENCE = 0x30,
        ARRAY_ELEMENT = 0xA0,
        STRING = 0x86,
        NONE = 0,
    }
}
