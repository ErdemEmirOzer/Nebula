using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.UI.StrategicMap;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Diagnostic: dumps exactly what the map puts on screen the moment it opens on a
    /// freshly booted campaign, so a "nothing is visible" report can be reproduced and
    /// read rather than guessed at.
    /// </summary>
    public class StrategicMapDiagnosticTests
    {
        private const string ScenePath = "Assets/Scenes/Nebula.unity";

        private static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
#endif
            yield return null;
            yield return null;

            // The scene boots to the main menu in production. An integration test wants
            // the world, so it asks for it -- explicitly, through the same idempotent
            // entry the menu\'s New Campaign button calls. No global flag, and nothing
            // that depends on which test ran first.
            Object.FindObjectOfType<Nebula.World.SolarSystemBootstrap>()?.BeginSession();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DumpWhatTheMapActuallyShowsOnAFreshBoot()
        {
            yield return LoadSceneFresh();
            var map = Object.FindObjectOfType<StrategicMapScreen>();
            NebulaContext context = Object.FindObjectOfType<SolarSystemBootstrap>().Context;

            map.SetOpen(true);
            yield return null;
            yield return null;

            var sb = new StringBuilder();
            sb.AppendLine("===== STRATEGIC MAP DIAGNOSTIC =====");
            sb.AppendLine($"screen resolution      : {Screen.width}x{Screen.height}");
            sb.AppendLine($"snapshot systems       : {map.Snapshot?.Systems.Count ?? -1}");
            sb.AppendLine($"snapshot links         : {map.Snapshot?.Links.Count ?? -1}");
            sb.AppendLine($"current system         : {map.Snapshot?.CurrentSystemId}");
            sb.AppendLine($"known levels in service: {CountKnown(context)}");

            if (map.Snapshot != null)
                foreach (StrategicSystemView s in map.Snapshot.Systems)
                    sb.AppendLine($"   system  {s.SystemId}  knowledge={s.Knowledge} posKnown={s.PositionKnown} details={s.DetailsKnown} planets={s.Planets.Count}");

            var canvas = map.GetComponentInChildren<Canvas>(true);
            sb.AppendLine($"canvas active/enabled  : {canvas.gameObject.activeInHierarchy}/{canvas.enabled} mode={canvas.renderMode} order={canvas.sortingOrder}");
            var scaler = canvas.GetComponent<CanvasScaler>();
            sb.AppendLine($"scaler                 : mode={scaler.uiScaleMode} ref={scaler.referenceResolution} match={scaler.matchWidthOrHeight} scaleFactor={canvas.scaleFactor:0.000}");

            // Every graphic that is genuinely being drawn right now.
            int visible = 0, invisibleAlpha = 0, zeroSize = 0;
            foreach (Graphic g in map.GetComponentsInChildren<Graphic>(false))
            {
                Rect r = g.rectTransform.rect;
                bool tiny = r.width < 1f || r.height < 1f;
                bool clear = g.color.a < 0.02f;
                if (tiny) zeroSize++;
                else if (clear) invisibleAlpha++;
                else visible++;

                string extra = g is Text t ? $" text=\"{Trim(t.text)}\" font={(t.font == null ? "NULL" : t.font.name)}" : "";
                if (visible + invisibleAlpha + zeroSize <= 40)
                    sb.AppendLine($"   {Path(g.transform)}  size={r.width:0}x{r.height:0} pos={g.rectTransform.anchoredPosition} a={g.color.a:0.00}{extra}");
            }
            sb.AppendLine($"graphics: drawn={visible} transparent={invisibleAlpha} zeroSized={zeroSize}");
            sb.AppendLine("====================================");

            UnityEngine.Debug.Log(sb.ToString());
            TestContext.WriteLine(sb.ToString());

            // The map must be showing something real, not a single stray box.
            Assert.Greater(visible, 3, "the open map must actually draw several elements");
            map.SetOpen(false);
        }

        private static int CountKnown(NebulaContext context)
        {
            int n = 0;
            foreach (var _ in context.NavigationDirector.Knowledge.All) n++;
            return n;
        }

        private static string Trim(string s) =>
            string.IsNullOrEmpty(s) ? "<empty>" : (s.Length > 40 ? s.Substring(0, 40) + "..." : s).Replace("\n", "\\n");

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p.name != "Root"; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
