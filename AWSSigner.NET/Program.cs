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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime.Internal.Util;

using NLog;
using NLog.Config;
using NLog.Targets;


namespace AWSSigner {
    class Program {
        private static readonly NLog.Logger Logger = LogManager.GetCurrentClassLogger();
        private static bool showDebugOutput = "TRUE" == ("" + Environment.GetEnvironmentVariable("AWSSIGNER_DEBUG")).ToUpper();
        static void Main(string[] args) {
#if DEBUG
            // DEBUG - EXPECT a file passed in, in ps1 'source' format that has all the env vars needed to set for AWS
            string envVarFile = args[0];

            // DEBUG - EXPECT a file passed in to sign in position three
            string fileToSignSource = args[1];
            string sourceFile = Path.GetFileNameWithoutExtension(fileToSignSource);
            string target = sourceFile + ".target.exe";
            File.Copy(fileToSignSource, target + ".exe", true);

            // DEBUG - position 3 represents
            string signingCert = args[2];
            Environment.SetEnvironmentVariable("SIGNING_CERT", signingCert);

            string signtoolPath = args[3];
            Environment.SetEnvironmentVariable("SIGNTOOL_PATH", signtoolPath);
            foreach (var line in File.ReadLines(envVarFile)) {
                var parts = line.Split('=');
                if (parts.Length == 2) {
                    var keyParts = parts[0].Split(':');
                    Environment.SetEnvironmentVariable(keyParts[1].Trim().Replace("\"", ""), parts[1].Trim().Replace("\"", ""));
                }
            }

            args = new string[] { fileToSignSource };
#endif
            try {
                var asm = Assembly.GetExecutingAssembly();
                var curdir = Path.GetDirectoryName(System.AppContext.BaseDirectory);
                var config = new LoggingConfiguration();
                // Targets where to log to: File and Console
                var logfile = new FileTarget("logfile") {
                    FileName = $"AWSSigner.log",
                    ArchiveEvery = FileArchivePeriod.Day,
                    ArchiveNumbering = ArchiveNumberingMode.Rolling,
                    MaxArchiveFiles = 7,
                    AutoFlush = true,
                    Layout = "[${date:universalTime=true:format=yyyy-MM-ddTHH:mm:ss.fff}Z] ${level:uppercase=true:padding=5}\t${logger}\t${message}\t${exception:format=tostring}",
                };
                var logconsole = new ConsoleTarget("logconsole");

                // Rules for mapping loggers to targets            
                config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
                config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);

                // Apply config           
                LogManager.Configuration = config;

                Logger.Info("========================= signing started =========================");
                Logger.Info("logger initialized");
                Logger.Info("    - name      : {0}", asm.GetName());
                Logger.Info("    - path      : {0}", curdir);
                Logger.Info("    - args      : {0}", String.Join(",", args));
                Logger.Info("========================================================================");

                if (args != null && args.Length > 0 && args[0].Contains(AppDomain.CurrentDomain.FriendlyName)) {
                    Logger.Debug("args[0] contains the AppDomain.CurrentDomain.FriendlyName, must be using dotnet run?");
                    args = args.Skip(1).ToArray();
                }

                bool argsValid = true;
                if (args.Length < 1) {
                    Logger.Info("Usage: signfile <file-to-sign>\n");
                    Logger.Info("ERROR: provide target file to sign and cert to use as arguments");
                    argsValid = false;
                }

                bool envVarsExist = VerifyEnvVar("AWS_KEY_ID");
                envVarsExist = VerifyEnvVar("AWS_ACCESS_KEY_ID");
                envVarsExist = VerifyEnvVar("AWS_REGION");
                envVarsExist = VerifyEnvVar("AWS_SECRET_ACCESS_KEY");
                envVarsExist = VerifyEnvVar("SIGNING_CERT");

                if (!argsValid || !envVarsExist) {
                    return;
                }

                bool filesExist = true;
                string fileToSign = String.Join(",", args); //have to join all the args when spaces are used???
                Logger.Info($"File to sign: '{fileToSign}'");
                if (!File.Exists(fileToSign)) {
                    Logger.Info($"File to sign doesn't exist: {fileToSign}");
                    filesExist = false;
                }
                string certToUse = Environment.GetEnvironmentVariable("SIGNING_CERT");
                if (!File.Exists(certToUse)) {
                    Logger.Info($"Cert to use doesn't exist : {certToUse}");
                    filesExist = false;
                }

                if (!filesExist) { return; }

                string exeAbsPath = Path.GetFullPath(fileToSign);
                string loc = Path.GetDirectoryName(exeAbsPath);

                string signToolPath = GetFullPath("signtool.exe");
                string awsKeyId = Environment.GetEnvironmentVariable("AWS_KEY_ID");

                if (!File.Exists(signToolPath)) {
                    string signToolPathEnv = Environment.GetEnvironmentVariable("SIGNTOOL_PATH");
                    if (!File.Exists(signToolPathEnv)) {
                        Logger.Info($"ERROR: Signtool not found on path and SIGNTOOL_PATH environment variable not set or file doesn't exist: {signToolPathEnv}!");
                        return;
                    } else {
                        Logger.Info($"Using signtool found via environment variable at: {signToolPathEnv}");
                        signToolPath = signToolPathEnv;
                    }
                }

                Logger.Info($"Using signtool   : {signToolPath}");
                Logger.Info($"Using cert       : {certToUse}");
                Logger.Info($"Signing file     : {fileToSign}");

                Logger.Debug("----- signFile: producing digest to send to AWS KMS -----");
                RunProcess(signToolPath, $"sign /dg {loc} /fd sha256 /f \"{certToUse}\" \"{exeAbsPath}\"");
                Logger.Info("  - digest file produced, sending to KMS for signing");

                byte[] tosign = Convert.FromBase64String(File.ReadAllText($"{exeAbsPath}.dig"));
                Logger.Debug("----- signFile: sending digest to AWS KMS for signing. -----");
                string signature = SignWithAwsKms(awsKeyId, tosign);
                File.WriteAllText($"{exeAbsPath}.dig.signed", signature);
                Logger.Info("  - digest signed, attaching signature");

                Logger.Debug($"----- signature len: {signature.Length} ----");
                Logger.Debug("----- done signing digest -----");
                Logger.Debug("----- signFile: adding signature -----");
                RunProcess(signToolPath, $"sign /di \"{loc}\" \"{exeAbsPath}\"");
                Logger.Info("  - signature attached, timestamping");

                Logger.Debug("----- signFile: adding timestamp -----");
                RunProcess(signToolPath, $"timestamp /tr http://timestamp.digicert.com /td sha256 \"{exeAbsPath}\"");
                Logger.Info("  - timestamped, verifying");

                RunProcess(signToolPath, $"verify /pa \"{exeAbsPath}\"");
                Logger.Info("  - verified, removing any files leftover from signing");
                DeleteFile($"{exeAbsPath}.dig");
                DeleteFile($"{exeAbsPath}.dig.signed");
                DeleteFile($"{exeAbsPath}.p7u");

                Logger.Info($"process complete. signed: {fileToSign}\n");
            } catch ( Exception e ) {
                Logger.Error($"FAILED TO SIGN REQUEST: {e.Message}");
                throw e;
            }
        }

        static void RunProcess(string fileName, string arguments) {
            Logger.Debug($"RunProcess Args: {fileName} {arguments}");
            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            if (!string.IsNullOrEmpty(output)) {
                Logger.Debug(output);
            }

            if (!string.IsNullOrEmpty(error)) {
                Logger.Error(error);
            }

            if (process.ExitCode != 0) {
                throw new Exception($"Process exited with code {process.ExitCode}: {error}");
            }
        }

        static string SignWithAwsKms(string keyId, byte[] digest) {
            var kmsClient = new AmazonKeyManagementServiceClient();
            var request = new SignRequest {
                KeyId = keyId,
                Message = new MemoryStream(digest),
                MessageType = MessageType.DIGEST,
                SigningAlgorithm = SigningAlgorithmSpec.RSASSA_PKCS1_V1_5_SHA_256
            };

            var response = kmsClient.Sign(request);
            if((int)response.HttpStatusCode > 300) {
                throw new Exception("signing data failed from aws kms! " + response.HttpStatusCode);
            }
            return Convert.ToBase64String(response.Signature.ToArray());
        }

        static void DeleteFile(string path) {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }

        public static bool ExistsOnPath(string fileName) {
            return GetFullPath(fileName) != null;
        }

        public static string GetFullPath(string fileName) {
            if (File.Exists(fileName))
                return Path.GetFullPath(fileName);

            string values = Environment.GetEnvironmentVariable("PATH");
            if (values == null) { return null; }
            foreach (var path in values.Split(Path.PathSeparator)) {
                var fullPath = Path.Combine(path, fileName);
                if (File.Exists(fullPath))
                    return Path.GetFullPath(fullPath);
            }
            return null;
        }

        public static bool VerifyEnvVar(string envVar) {
            var val = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(val)) {
                Logger.Info($"ERROR: Environment variable must be set: {envVar}");
                return false;
            }
#if DEBUG
            Logger.Info($"INFO: Environment variable {envVar}={val}");
#endif
            return true;
        }
    }
}