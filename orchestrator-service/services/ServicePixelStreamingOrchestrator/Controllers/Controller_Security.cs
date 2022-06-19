/// Copyright 2022- Burak Kara, All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp;
using SixLaborsCaptcha.Core;

namespace ServicePixelStreamingOrchestrator.Controllers
{
    internal class Controller_Security
    {
        private static Controller_Security Instance = null;
        public static Controller_Security Get()
        {
            if (Instance == null)
            {
                Instance = new Controller_Security();
            }
            return Instance;
        }
        private Controller_Security() 
        {
        }
        ~Controller_Security()
        {
            bRunning = false;
        }
        private bool bRunning = true;

        public const string CAPTCHA_SESSION_SECRET_COOKIE_NAME = "ps-captcha-session-secret-cookie";
        public const int CAPTCHA_SESSION_SECRET_COOKIE_EXPIRES_AFTER_MINUTES = 60;
        public const int CAPTCHA_SESSION_SECRET_COOKIE_EXPIRES_AFTER_MINUTES_IF_NO_WS_CONNECTION = 5; //Due to VM open times etc.

        public const string CAPTCHA_CODE_VERIFICATION_COOKIE_NAME = "ps-captcha-code-verification-cookie";
        public const int CAPTCHA_CODE_VERIFICATION_COOKIE_EXPIRES_AFTER_MINUTES = 2;

        private static readonly string[] DEJAVU_FONT_FAMILY = new string[] { "DejaVu Sans Mono", "DejaVu LGC Sans Mono"/*, "DejaVu LGC Sans Condensed", "DejaVu Serif", "DejaVu Sans Condensed", "DejaVu LGC Sans", "DejaVu Sans", "DejaVu Serif Condensed", "DejaVu LGC Serif", "DejaVu LGC Serif Condensed", "DejaVu Sans Light", "DejaVu LGC Sans Light"*/ };

        public bool Initialize(Action<string> _ErrorMessageAction)
        {
            // Captcha Page Template Setup
            var AbsolutePathToCaptchaPageTemplate = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "public/captcha_page.html");
            if (!File.Exists(AbsolutePathToCaptchaPageTemplate))
            {
                _ErrorMessageAction?.Invoke($"{AbsolutePathToCaptchaPageTemplate} does not exists in the local file system.");
                return false;
            }
            try
            {
                CaptchaPageTemplate = File.ReadAllText(AbsolutePathToCaptchaPageTemplate);
            }
            catch (Exception e)
            {
                _ErrorMessageAction?.Invoke($"Reading {AbsolutePathToCaptchaPageTemplate} has failed with {e.Message}");
                return false;
            }

            CaptchaPageTemplate = CaptchaPageTemplate
                .Replace("${{CAPTCHA_CODE_VERIFICATION_COOKIE_NAME}}", CAPTCHA_CODE_VERIFICATION_COOKIE_NAME)
                .Replace("${{CAPTCHA_CODE_VERIFICATION_COOKIE_EXPIRES_AFTER_MINUTES}}", $"{CAPTCHA_CODE_VERIFICATION_COOKIE_EXPIRES_AFTER_MINUTES}");

            CaptchaPageImageReplacementStartIndex = CaptchaPageTemplate.IndexOf(CaptchaPageImageReplacementLookupKey);

            TickerThread = new Thread(TickerThreadRunnable);
            TickerThread.Start();

            return true;
        }
        private string CaptchaPageTemplate;
        private const string CaptchaPageImageReplacementLookupKey = "${{IMAGE_BASE_64}}";
        private int CaptchaPageImageReplacementStartIndex;
        private readonly int CaptchaPageImageReplacementLength = CaptchaPageImageReplacementLookupKey.Length;

        public bool VerifyCaptchaSession(out string _UserId_OnSuccess, CookieCollection _Cookies, bool _bFromWebSocket)
        {
            _UserId_OnSuccess = null;

            var CaptchaCookie = _Cookies[CAPTCHA_SESSION_SECRET_COOKIE_NAME];
            if (CaptchaCookie == null) return false;

            lock (CaptchaSessionTSMap_NotThreadSafe)
            {
                if (!CaptchaSessionTSMap_NotThreadSafe.ContainsKey(CaptchaCookie.Value)) return false;

                if (_bFromWebSocket)
                {
                    var CreatedTS = CaptchaSessionTSMap_NotThreadSafe[CaptchaCookie.Value].Item2;
                    CaptchaSessionTSMap_NotThreadSafe[CaptchaCookie.Value] = new Tuple<bool, long>(true, CreatedTS);
                }
                _UserId_OnSuccess = CaptchaCookie.Value;
                return true;
            }
        }
        
        public void RemoveCaptchaSession(CookieCollection _Cookies)
        {
            var CaptchaCookie = _Cookies[CAPTCHA_SESSION_SECRET_COOKIE_NAME];
            if (CaptchaCookie == null) return;

            lock (CaptchaSessionTSMap_NotThreadSafe)
            {
                CaptchaSessionTSMap_NotThreadSafe.Remove(CaptchaCookie.Value);
            }
        }

        public string GenerateAndStoreCaptchaCode_ReturnCaptchaPage()
        {
            var CaptchaModule = new SixLaborsCaptchaModule(new SixLaborsCaptchaOptions
            {
                FontFamilies = DEJAVU_FONT_FAMILY,
                DrawLines = 18,
                TextColor = new Color[] { Color.Blue, Color.Black },
                Width = 274,
                Height = 137,
                FontSize = 72,
                NoiseRate = 6400
            });

            string Code;

            lock (CodeTSMap_NotThreadSafe)
            {
                do
                {
                    Code = Extentions.GetUniqueKey(5);

                } while (CodeTSMap_NotThreadSafe.ContainsKey(Code));

                CodeTSMap_NotThreadSafe.Add(Code, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds());
            }

            var AsByteArray = CaptchaModule.Generate(Code);

            var AsStringBuilder = new StringBuilder(CaptchaPageTemplate);
            AsStringBuilder.Remove(CaptchaPageImageReplacementStartIndex, CaptchaPageImageReplacementLength);
            AsStringBuilder.Insert(CaptchaPageImageReplacementStartIndex, Convert.ToBase64String(AsByteArray));
            return AsStringBuilder.ToString();
        }

        public bool VerifyCaptchaCode(CookieCollection _Cookies, out string _GeneratedSessionSecret)
        {
            _GeneratedSessionSecret = null;

            var CaptchaVerificationCookie = _Cookies[CAPTCHA_CODE_VERIFICATION_COOKIE_NAME];
            if (CaptchaVerificationCookie == null) return false;

            lock (CodeTSMap_NotThreadSafe)
            {
                if (!CodeTSMap_NotThreadSafe.Remove(CaptchaVerificationCookie.Value))
                {
                    return false;
                }
            }

            lock (CaptchaSessionTSMap_NotThreadSafe)
            {
                do
                {
                    _GeneratedSessionSecret = new string(Enumerable.Repeat(RandomizerChars, 32).Select(s => s[Randomizer.Next(s.Length)]).ToArray());

                } while (CaptchaSessionTSMap_NotThreadSafe.ContainsKey(_GeneratedSessionSecret));

                CaptchaSessionTSMap_NotThreadSafe.Add(_GeneratedSessionSecret, new Tuple<bool, long>(false/*bAccessedByWS*/, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()));
            }

            return true;
        }
        private readonly Dictionary<string, long> CodeTSMap_NotThreadSafe = new Dictionary<string, long>();
        private readonly Dictionary<string, Tuple<bool/*bAccessedByWS*/, long>> CaptchaSessionTSMap_NotThreadSafe = new Dictionary<string, Tuple<bool, long>>();
        private readonly Random Randomizer = new Random();
        private const string RandomizerChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        private void TickerThreadRunnable()
        {
            Thread.CurrentThread.IsBackground = true;

            while (bRunning)
            {
                Thread.Sleep(1000);

                lock (CodeTSMap_NotThreadSafe)
                {
                    var CodesToBeRemoved = new List<string>();

                    foreach (var CurrentPair in CodeTSMap_NotThreadSafe)
                    {
                        if (DateTimeOffset.FromUnixTimeSeconds(CurrentPair.Value).AddMinutes(CAPTCHA_CODE_VERIFICATION_COOKIE_EXPIRES_AFTER_MINUTES) <= new DateTimeOffset(DateTime.UtcNow))
                        {
                            CodesToBeRemoved.Add(CurrentPair.Key);
                        }
                    }

                    foreach (var CodeToRemove in CodesToBeRemoved)
                    {
                        CodeTSMap_NotThreadSafe.Remove(CodeToRemove);
                    }
                }

                lock (CaptchaSessionTSMap_NotThreadSafe)
                {
                    var SessionsToBeRemoved = new List<string>();

                    foreach (var CurrentPair in CaptchaSessionTSMap_NotThreadSafe)
                    {
                        if (!CurrentPair.Value.Item1/*bAccessedByWS*/ && DateTimeOffset.FromUnixTimeSeconds(CurrentPair.Value.Item2).AddMinutes(CAPTCHA_SESSION_SECRET_COOKIE_EXPIRES_AFTER_MINUTES_IF_NO_WS_CONNECTION) <= new DateTimeOffset(DateTime.UtcNow))
                        {
                            SessionsToBeRemoved.Add(CurrentPair.Key);
                        }
                    }

                    foreach (var SessionToRemove in SessionsToBeRemoved)
                    {
                        CaptchaSessionTSMap_NotThreadSafe.Remove(SessionToRemove);
                    }
                }
            }
        }
        private Thread TickerThread;
    }
}