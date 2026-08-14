using Nebula.Extraction;
using Nebula.UI.Design;

namespace Nebula.UI.Bench
{
    /// <summary>
    /// The processing bench's refusals, in the player's own language.
    /// <para>
    /// One table per typed enum, and every member of every enum is covered. A
    /// refusal that fell through to a default would put an enum name in front of a
    /// player; a refusal that returned an empty string would put nothing there at
    /// all, which is the failure this whole page was rebuilt to remove — a control
    /// that quietly does nothing.
    /// </para>
    /// <para>
    /// The mapping lives here rather than in the page because both the drag path
    /// and the keyboard path show the same words for the same refusal, and because
    /// a table with no Unity types in it can be asserted exhaustively from an
    /// EditMode fixture without building a canvas.
    /// </para>
    /// </summary>
    public static class NebulaBenchMessages
    {
        /// <summary>
        /// What a v14 save's rescued output has to be dealt with before anything
        /// else can move. Not a member of any of the three enums: it is a state of
        /// the bench, checked before a command is even attempted.
        /// </summary>
        public const string LegacyClaimPending = "ÖNCE BEKLEYEN ÇIKTIYI AL";

        /// <summary>Shown on the recipe column while the bench is empty.</summary>
        public const string BenchEmptyHint = "ÖNCE TEZGÂHA BİR KAYNAK BIRAK";

        /// <summary>Shown in the drop zone while the bench is empty.</summary>
        public const string DropHint = "İŞLENECEK KAYNAĞI BURAYA SÜRÜKLE";

        /// <summary>Why the hold could not give this up. Every member is covered.</summary>
        public static string Deposit(BenchDepositReason reason)
        {
            switch (reason)
            {
                case BenchDepositReason.None: return string.Empty;
                case BenchDepositReason.NoSession: return "OTURUM YOK";
                case BenchDepositReason.NoCargo: return "GEMİ KARGOSU YOK";
                case BenchDepositReason.InvalidAmount: return "GEÇERSİZ MİKTAR";
                case BenchDepositReason.UnknownItem: return "BİLİNMEYEN KAYNAK";
                case BenchDepositReason.ItemNotProcessable: return "BU ÜRÜN İŞLENEMEZ";
                case BenchDepositReason.InsufficientCargo: return "KARGODA YETERLİ KAYNAK YOK";
                case BenchDepositReason.DifferentInputAlreadyLoaded:
                    return "TEZGÂHTA FARKLI BİR KAYNAK VAR";
                case BenchDepositReason.Overflow: return "MİKTAR TAŞIYOR — DAHA KÜÇÜK BİR SAYI GİR";
                default: return "ŞU AN TEZGÂHA EKLENEMEZ";
            }
        }

        /// <summary>Why the bench could not hand this back. Every member is covered.</summary>
        public static string Withdraw(BenchWithdrawReason reason)
        {
            switch (reason)
            {
                case BenchWithdrawReason.None: return string.Empty;
                case BenchWithdrawReason.NoSession: return "OTURUM YOK";
                case BenchWithdrawReason.NoCargo: return "GEMİ KARGOSU YOK";
                case BenchWithdrawReason.InvalidAmount: return "GEÇERSİZ MİKTAR";
                case BenchWithdrawReason.BenchEmpty: return "TEZGÂH BOŞ";
                case BenchWithdrawReason.InsufficientBenchInput: return "YETERSİZ TEZGÂH GİRDİSİ";
                case BenchWithdrawReason.NoCargoRoom: return "KARGO ALANI YOK — GERİ ALINAMIYOR";
                default: return "ŞU AN GERİ ALINAMAZ";
            }
        }

        /// <summary>
        /// Why the conversion did not happen. Every member is covered.
        /// <para>
        /// <paramref name="recipe"/> is what lets the multiple refusal name the
        /// number the player has to hit; without it the message could only say
        /// "wrong amount", which is a refusal the player cannot act on.
        /// </para>
        /// </summary>
        public static string Convert(BenchConvertReason reason, ProcessingRecipe recipe = null)
        {
            switch (reason)
            {
                case BenchConvertReason.None: return string.Empty;
                case BenchConvertReason.NoSession: return "OTURUM YOK";
                case BenchConvertReason.NoCargo: return "GEMİ KARGOSU YOK";
                case BenchConvertReason.InvalidAmount: return "GEÇERSİZ MİKTAR";
                case BenchConvertReason.BenchEmpty: return "TEZGÂH BOŞ";
                case BenchConvertReason.UnknownRecipe: return "BİLİNMEYEN TARİF";
                case BenchConvertReason.RecipeInputMismatch:
                    return "TARİF TEZGÂHTAKİ KAYNAĞA UYMUYOR";
                case BenchConvertReason.AmountNotMultipleOfRecipeInput:
                    return recipe == null
                        ? "MİKTAR TARİFİN GİRDİSİNİN TAM KATI OLMALI"
                        : $"MİKTAR {Genitive(recipe.InputCount)} TAM KATI OLMALI";
                case BenchConvertReason.InsufficientBenchInput: return "YETERSİZ TEZGÂH GİRDİSİ";
                case BenchConvertReason.OutputNoRoom: return "ÇIKTI İÇİN KARGO ALANI YOK";
                case BenchConvertReason.Overflow: return "MİKTAR TAŞIYOR — DAHA KÜÇÜK BİR SAYI GİR";
                default: return "ŞU AN DÖNÜŞTÜRÜLEMEZ";
            }
        }

        /// <summary>
        /// A number with its Turkish genitive suffix, upper-cased: <c>3</c> becomes
        /// <c>3'ÜN</c>, <c>2</c> becomes <c>2'NİN</c>.
        /// <para>
        /// A table rather than a vowel-harmony rule, because the suffix follows how
        /// the numeral is <i>spoken</i> and the digits do not carry that: 3 (üç) and
        /// 4 (dört) both take ÜN while 5 (beş) takes İN. Written out, the refusal
        /// reads like a sentence a person wrote; concatenated blindly it reads like
        /// a bug. The recipes in the catalog use 2 and 3, and the table covers the
        /// whole spoken range so a new ratio cannot produce broken Turkish.
        /// </para>
        /// </summary>
        internal static string Genitive(int value)
        {
            if (value < 0) return value.ToString();

            // Round hundreds and thousands are spoken as "yüz"/"bin" and take their
            // own suffix regardless of the digits in front of them.
            if (value >= 1000 && value % 1000 == 0) return value + "'İN";
            if (value >= 100 && value % 100 == 0) return value + "'ÜN";

            int tens = value % 100;
            if (value >= 10 && tens % 10 == 0)
            {
                switch (tens)
                {
                    case 10: return value + "'UN";   // on
                    case 20: return value + "'NİN";  // yirmi
                    case 30: return value + "'UN";   // otuz
                    case 40: return value + "'IN";   // kırk
                    case 50: return value + "'NİN";  // elli
                    case 60: return value + "'IN";   // altmış
                    case 70: return value + "'İN";   // yetmiş
                    case 80: return value + "'İN";   // seksen
                    case 90: return value + "'IN";   // doksan
                }
            }

            switch (value % 10)
            {
                case 1: return value + "'İN";   // bir
                case 2: return value + "'NİN";  // iki
                case 3: return value + "'ÜN";   // üç
                case 4: return value + "'ÜN";   // dört
                case 5: return value + "'İN";   // beş
                case 6: return value + "'NIN";  // altı
                case 7: return value + "'NİN";  // yedi
                case 8: return value + "'İN";   // sekiz
                case 9: return value + "'UN";   // dokuz
                default: return value + "'IN";
            }
        }

        /// <summary>An item's catalog name, upper-cased for the interface's voice.</summary>
        public static string ItemName(string itemId) =>
            NebulaText.Upper(ProcessingCatalog.ItemDisplayName(itemId));
    }
}
