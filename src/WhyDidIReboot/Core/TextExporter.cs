using System.Text;

namespace WhyDidIReboot.Core;

/// <summary>Plain text and CSV renderings of an analysis, shared by the UI export and the command line.</summary>
public static class TextExporter
{
    public static string ToText(AnalysisResult result, IEnumerable<RebootEntry> entries, string rangeLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Why Did I Reboot — report for {(result.Source.IsOffline ? "logs at " + result.Source.Display : Environment.MachineName)}");
        sb.AppendLine($"Generated {Format.When(DateTime.Now)} · Range: {rangeLabel} · Log records read: {result.RecordsRead}");
        sb.AppendLine(result.Source.IsOffline
            ? $"Offline logs from another Windows installation. Last recorded boot: {Format.When(result.CurrentBootTime)}"
            : $"Current session: up since {Format.When(result.CurrentBootTime)} ({Format.Duration(DateTime.Now - result.CurrentBootTime)})");
        foreach (var w in result.Warnings) sb.AppendLine($"Warning: {w}");
        sb.AppendLine();

        foreach (var e in entries)
        {
            sb.AppendLine(EntryToText(e));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string EntryToText(RebootEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}]  {e.Title.ToUpperInvariant()}");
        sb.AppendLine($"    Category: {Format.Category(e.Category)}");
        sb.AppendLine($"    {e.Summary}");
        if (e.Updates.Count > 0)
        {
            sb.AppendLine("    Installed updates:");
            foreach (var g in e.UpdateGroups)
            {
                sb.AppendLine($"      {g.Name}:");
                foreach (var u in g.Items)
                {
                    sb.AppendLine($"        - {u.Title}{(u.Failed ? " (FAILED)" : "")}");
                    if (u.Description is not null) sb.AppendLine($"          {u.Description}");
                    if (u.Url is not null) sb.AppendLine($"          {u.Url}");
                }
            }
        }
        foreach (var d in e.Details) sb.AppendLine($"    {d.Key}: {d.Value}");
        foreach (var l in e.Links) sb.AppendLine($"    {l.Label}: {l.Url}");
        if (e.Evidence.Count > 0)
        {
            sb.AppendLine("    Log records:");
            foreach (var ev in e.Evidence)
                sb.AppendLine($"      - {ev.Time:yyyy-MM-dd HH:mm:ss}  {ev.Log} #{ev.Id} {ev.Provider}: {Truncate(ev.Message, 300)}");
        }
        return sb.ToString().TrimEnd();
    }

    public static string ToCsv(IEnumerable<RebootEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Category,Title,Summary,ShutdownTime,BootTime,DowntimeSeconds,PreviousUptimeSeconds,DumpPath");
        foreach (var e in entries)
        {
            sb.Append(Csv(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
              .Append(Csv(Format.Category(e.Category))).Append(',')
              .Append(Csv(e.Title)).Append(',')
              .Append(Csv(e.Summary)).Append(',')
              .Append(Csv(e.ShutdownTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")).Append(',')
              .Append(Csv(e.BootTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")).Append(',')
              .Append(e.Downtime is TimeSpan d ? ((long)d.TotalSeconds).ToString() : "").Append(',')
              .Append(e.PreviousUptime is TimeSpan u ? ((long)u.TotalSeconds).ToString() : "").Append(',')
              .Append(Csv(e.DumpPath ?? ""))
              .AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>A self-contained HTML report that mirrors the app's cards and follows the viewer's light/dark preference.</summary>
    public static string ToHtml(AnalysisResult result, IEnumerable<RebootEntry> entries, string rangeLabel)
    {
        var list = entries.ToList();
        var sb = new StringBuilder();
        var machine = H(Environment.MachineName);

        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
        sb.Append("<title>Why Did I Reboot — ").Append(machine).Append("</title>\n<style>\n");
        sb.Append(@"
:root{color-scheme:light dark;--bg:#f3f4f6;--card:#fff;--border:#e5e7eb;--text:#111827;--text2:#374151;--muted:#6b7280;--faint:#9ca3af;--evidence:#f9fafb;--link:#2563eb;--banner:#eff6ff;--banner-b:#bfdbfe;--banner-t:#1e3a8a;--warn:#b45309;--hl:#fde68a;--hlfg:#1f1300}
@media (prefers-color-scheme:dark){:root{--bg:#111318;--card:#1b1e25;--border:#2c3039;--text:#e6e8ee;--text2:#c0c5cf;--muted:#8b919e;--faint:#6b7280;--evidence:#14171d;--link:#6ea0ff;--banner:#172033;--banner-b:#27406b;--banner-t:#bfd3ff;--warn:#f0b45a;--hl:#7a5a0c;--hlfg:#fff3c4}}
*{box-sizing:border-box}
body{margin:0;padding:32px 20px;background:var(--bg);color:var(--text);font:14px/1.45 ""Segoe UI Variable Text"",""Segoe UI"",system-ui,-apple-system,sans-serif}
.wrap{max-width:980px;margin:0 auto}
h1{font-size:26px;font-weight:600;margin:0 0 4px}
.sub{color:var(--muted);margin:0 0 18px;font-size:13px}
.banner{background:var(--banner);border:1px solid var(--banner-b);color:var(--banner-t);border-radius:8px;padding:10px 14px;margin-bottom:14px}
.warn{color:var(--warn);font-size:13px;margin-top:4px}
.bar{display:flex;flex-wrap:wrap;gap:10px;align-items:flex-start;margin:0 0 6px}
.counts{display:flex;flex-wrap:wrap;gap:8px;flex:1}
.chip{--col:var(--c);display:inline-flex;align-items:center;gap:8px;border:1px solid var(--col);background:color-mix(in srgb,var(--col) 12%,transparent);border-radius:14px;padding:4px 10px;font:inherit;font-size:12px;color:var(--text);cursor:pointer}
.chip b{background:var(--col);color:#fff;border-radius:9px;padding:0 7px;font-size:11px}
.chip.off{border-color:var(--border);background:var(--evidence);color:var(--faint)}
.chip.off b{background:var(--border);color:var(--muted)}
.search{position:relative;flex:none}
.search input{width:270px;padding:6px 30px 6px 10px;border:1px solid var(--border);border-radius:6px;background:var(--card);color:var(--text);font:inherit;font-size:13px}
.search button{position:absolute;right:4px;top:50%;transform:translateY(-50%);border:0;background:none;color:var(--muted);cursor:pointer;font-size:13px;padding:2px 6px}
.presets{display:flex;flex-wrap:wrap;gap:12px;font-size:12px;color:var(--muted);margin:0 0 18px}
.presets a{color:var(--link);text-decoration:none}
.presets a:hover{text-decoration:underline}
#shown{margin-left:auto}
mark.hl{background:var(--hl);color:var(--hlfg);font-weight:600;border-radius:2px;padding:0 1px}
.empty{color:var(--muted);text-align:center;padding:40px 0}
[hidden]{display:none!important}
@media print{.bar,.presets{display:none}}
h2.day{font-size:13px;font-weight:600;color:var(--muted);margin:26px 0 10px}
.card{--col:var(--c);background:var(--card);border:1px solid var(--border);border-left:6px solid var(--col);border-radius:10px;padding:14px 18px;margin-bottom:12px;box-shadow:0 1px 3px rgba(0,0,0,.05)}
@media (prefers-color-scheme:dark){.chip,.card{--col:var(--cd)}}
.head{display:flex;align-items:flex-start;gap:12px}
.dot{flex:none;width:36px;height:36px;border-radius:50%;background:color-mix(in srgb,var(--col) 14%,transparent);position:relative}
.dot::after{content:"""";position:absolute;inset:12px;border-radius:50%;background:var(--col)}
.title{font-size:16px;font-weight:600;line-height:1.3}
.when{font-size:12px;color:var(--muted);margin-top:2px}
.badge{margin-left:auto;flex:none;font-size:11px;font-weight:600;padding:3px 8px;border-radius:10px;background:color-mix(in srgb,var(--col) 14%,transparent);color:var(--col)}
.summary{color:var(--text2);margin:10px 0 0 48px}
.uhead{margin:10px 0 0 48px;font-weight:600;color:var(--text2);font-size:13px}
.ugroup{margin:6px 0 0 48px;font-size:11px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:var(--muted)}
.updates{margin:2px 0 0 48px;padding-left:18px;font-size:13px}
.updates li{margin:4px 0}
.updates .desc{color:var(--text2);font-size:12.5px}
.updates .cat{color:var(--muted);font-size:11px;margin-left:6px}
.updates .fail{color:#dc2626;font-weight:600;font-size:11px;margin-left:6px}
.updates a,.links a{color:var(--link);text-decoration:none;font-size:12px;margin-right:14px}
.updates a:hover,.links a:hover{text-decoration:underline}
.links{margin:8px 0 0 48px}
details{margin:8px 0 0 48px;font-size:12.5px}
summary{cursor:pointer;color:var(--link);user-select:none}
table.kv{border-collapse:collapse;margin-top:8px}
.kv td{padding:2px 14px 2px 0;vertical-align:top}
.kv td:first-child{color:var(--muted);white-space:nowrap}
.logs{margin-top:10px;font-weight:600;color:var(--text2)}
.ev{background:var(--evidence);border-radius:6px;padding:6px 10px;margin-top:6px}
.ev .meta{color:var(--muted);font-size:11px}
.ev .msg{font:11.5px/1.4 ""Cascadia Mono"",Consolas,monospace;white-space:pre-wrap;word-break:break-word;margin-top:2px}
.foot{color:var(--faint);font-size:12px;margin-top:28px}
@media print{body{padding:0;background:#fff}.card{break-inside:avoid;box-shadow:none}details{display:block}details>summary{display:none}details>*{display:block}}
");
        sb.Append("</style>\n</head>\n<body>\n<div class=\"wrap\">\n");
        sb.Append("<h1>Why Did I Reboot</h1>\n");
        var subject = result.Source.IsOffline ? "logs at " + H(result.Source.Display) : machine;
        sb.Append($"<p class=\"sub\">Report for <b>{subject}</b> · generated {H(Format.When(DateTime.Now))} · range: {H(rangeLabel)} · {result.RecordsRead:N0} log records read</p>\n");
        sb.Append("<div class=\"banner\">");
        sb.Append(result.Source.IsOffline
            ? $"Offline logs from another Windows installation. Last recorded boot: {H(Format.When(result.CurrentBootTime))}."
            : $"This PC has been running since {H(Format.When(result.CurrentBootTime))} ({H(Format.Duration(DateTime.Now - result.CurrentBootTime))} ago).");
        foreach (var w in result.Warnings) sb.Append($"<div class=\"warn\">{H(w)}</div>");
        sb.Append("</div>\n");

        // Filter bar: category chips (click to toggle), presets, and a search box, all driven by the inline script below.
        sb.Append("<div class=\"bar\"><div class=\"counts\">");
        foreach (var group in list.GroupBy(e => e.Category).OrderByDescending(g => g.Count()))
            sb.Append($"<button type=\"button\" class=\"chip\" data-cat=\"{group.Key}\" style=\"--c:{CategoryPalette.Hex(group.Key, false)};--cd:{CategoryPalette.Hex(group.Key, true)}\" title=\"Click to show or hide\">{H(Format.Category(group.Key))} <b>{group.Count()}</b></button>");
        sb.Append("</div>");
        sb.Append("<div class=\"search\"><input id=\"q\" type=\"search\" placeholder=\"Search titles, KB numbers, STOP codes…\" aria-label=\"Search\"><button id=\"clear\" type=\"button\" title=\"Clear search\" hidden>✕</button></div>");
        sb.Append("</div>\n");
        sb.Append("<div class=\"presets\"><span>Show:</span>");
        foreach (var (key, label) in new[] { ("default", "default"), ("everything", "everything"), ("reboots", "reboots only"), ("problems", "problems only"), ("none", "none") })
            sb.Append($"<a href=\"#\" data-preset=\"{key}\">{label}</a>");
        sb.Append("<span id=\"shown\"></span></div>\n");

        if (list.Count == 0) sb.Append("<p class=\"sub\">No events in this range.</p>\n");
        sb.Append("<div id=\"empty\" class=\"empty\" hidden>Nothing matches the current filters.</div>\n");

        string? day = null;
        foreach (var e in list)
        {
            if (e.DateGroup != day)
            {
                day = e.DateGroup;
                sb.Append($"<h2 class=\"day\">{H(day)}</h2>\n");
            }

            var when = new List<string> { Format.When(e.Timestamp) };
            if (e.IsReboot)
            {
                if (e.ShutdownTime is null && e.BootTime is not null) when[0] = "Back up at " + Format.When(e.BootTime);
                if (e.Downtime is TimeSpan d) when.Add("down for " + Format.Duration(d));
                if (e.PreviousUptime is TimeSpan u) when.Add("previous session ran " + Format.Duration(u));
            }

            sb.Append($"<div class=\"card\" data-cat=\"{e.Category}\" data-reboot=\"{(e.IsReboot ? 1 : 0)}\" style=\"--c:{CategoryPalette.Hex(e.Category, false)};--cd:{CategoryPalette.Hex(e.Category, true)}\">\n");
            sb.Append("<div class=\"head\"><div class=\"dot\"></div><div>");
            sb.Append($"<div class=\"title\">{H(e.Title)}</div><div class=\"when\">{H(string.Join("  ·  ", when))}</div></div>");
            sb.Append($"<span class=\"badge\">{H(Format.Category(e.Category))}</span></div>\n");
            sb.Append($"<div class=\"summary\">{H(e.Summary)}</div>\n");
            if (e.Updates.Count > 0)
            {
                // Collapsed by default, like the app; the script opens it when a search term is inside.
                sb.Append("<details class=\"updates-block\"><summary>Installed updates</summary>\n");
                foreach (var g in e.UpdateGroups)
                {
                    sb.Append($"<div class=\"ugroup\">{H(g.Name)}</div><ul class=\"updates\">");
                    foreach (var u in g.Items)
                    {
                        sb.Append("<li><b>").Append(H(u.Title)).Append("</b>");
                        if (u.Failed) sb.Append(" <span class=\"fail\">failed</span>");
                        if (u.Category is not null) sb.Append($" <span class=\"cat\">{H(u.Category)}</span>");
                        if (u.Description is not null) sb.Append($"<div class=\"desc\">{H(u.Description)}</div>");
                        if (u.Url is not null) sb.Append($"<a href=\"{H(u.Url)}\" target=\"_blank\" rel=\"noopener\">{H(u.LinkLabel)} ↗</a>");
                        sb.Append("</li>");
                    }
                    sb.Append("</ul>\n");
                }
                sb.Append("</details>\n");
            }
            if (e.Links.Count > 0)
            {
                sb.Append("<div class=\"links\">");
                foreach (var l in e.Links) sb.Append($"<a href=\"{H(l.Url)}\" target=\"_blank\" rel=\"noopener\">{H(l.Label)} ↗</a>");
                sb.Append("</div>\n");
            }
            sb.Append("<details><summary>Details and log records</summary>\n<table class=\"kv\">");
            foreach (var d in e.Details) sb.Append($"<tr><td>{H(d.Key)}</td><td>{H(d.Value)}</td></tr>");
            sb.Append("</table>\n");
            if (e.Evidence.Count > 0)
            {
                sb.Append("<div class=\"logs\">Log records</div>\n");
                foreach (var ev in e.Evidence)
                    sb.Append($"<div class=\"ev\"><div class=\"meta\"><b>{ev.Time:d MMM yyyy HH:mm:ss}</b> · {H(ev.Log)} · Event {ev.Id} · {H(ev.Provider)}</div><div class=\"msg\">{H(ev.Message)}</div></div>\n");
            }
            sb.Append("</details>\n</div>\n");
        }

        sb.Append("<p class=\"foot\">Generated by Why Did I Reboot. Categories are inferred from the Windows System and Application event logs. Open with <code>?q=words</code> in the address bar to start with a search.</p>\n");
        sb.Append("</div>\n<script>\n").Append(Script).Append("\n</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>
    /// Filtering, search, highlighting and auto-expansion for the HTML report. Everything stays in the
    /// page: chips toggle categories, presets mirror the app, every search word must appear somewhere on a
    /// card, matches are wrapped in &lt;mark&gt;, and a collapsed block opens when a match sits inside it.
    /// </summary>
    private const string Script = """
(function(){
var cards=Array.prototype.slice.call(document.querySelectorAll('.card'));
var chips=Array.prototype.slice.call(document.querySelectorAll('.chip[data-cat]'));
var q=document.getElementById('q'),clear=document.getElementById('clear'),shown=document.getElementById('shown'),empty=document.getElementById('empty');
var presets={
  'default':function(c){return c!=='Sleep';},
  'everything':function(){return true;},
  'reboots':function(c){return c!=='Sleep'&&c!=='LiveKernelEvent';},
  'problems':function(c){return ['BlueScreen','Unexpected','LiveKernelEvent','Unknown'].indexOf(c)>=0;},
  'none':function(){return false;}
};
var on={};chips.forEach(function(ch){on[ch.getAttribute('data-cat')]=true;});
cards.forEach(function(c){c.setAttribute('data-text',c.textContent.toLowerCase());});
function unmark(el){
  Array.prototype.slice.call(el.querySelectorAll('mark.hl')).forEach(function(m){m.parentNode.replaceChild(document.createTextNode(m.textContent),m);});
  el.normalize();
}
function mark(el,terms){
  if(!terms.length)return;
  var walker=document.createTreeWalker(el,NodeFilter.SHOW_TEXT,null,false),nodes=[],n;
  while((n=walker.nextNode()))nodes.push(n);
  nodes.forEach(function(node){
    var text=node.nodeValue,lower=text.toLowerCase(),ranges=[];
    terms.forEach(function(t){var i=0;while((i=lower.indexOf(t,i))>=0){ranges.push([i,i+t.length]);i+=1;}});
    if(!ranges.length)return;
    ranges.sort(function(a,b){return a[0]-b[0];});
    var merged=[];ranges.forEach(function(r){var last=merged[merged.length-1];if(last&&r[0]<=last[1])last[1]=Math.max(last[1],r[1]);else merged.push(r.slice());});
    var frag=document.createDocumentFragment(),pos=0;
    merged.forEach(function(r){if(r[0]>pos)frag.appendChild(document.createTextNode(text.slice(pos,r[0])));var m=document.createElement('mark');m.className='hl';m.textContent=text.slice(r[0],r[1]);frag.appendChild(m);pos=r[1];});
    if(pos<text.length)frag.appendChild(document.createTextNode(text.slice(pos)));
    node.parentNode.replaceChild(frag,node);
  });
}
function apply(){
  var terms=q.value.toLowerCase().split(/\s+/).filter(Boolean),count=0;
  cards.forEach(function(c){
    var text=c.getAttribute('data-text');
    var vis=!!on[c.getAttribute('data-cat')]&&terms.every(function(t){return text.indexOf(t)>=0;});
    c.hidden=!vis;
    unmark(c);
    if(vis){mark(c,terms);count++;}
    Array.prototype.slice.call(c.querySelectorAll('details')).forEach(function(d){d.open=terms.length?!!d.querySelector('mark.hl'):false;});
  });
  Array.prototype.slice.call(document.querySelectorAll('h2.day')).forEach(function(h){
    var e=h.nextElementSibling,any=false;
    while(e&&!e.classList.contains('day')){if(e.classList.contains('card')&&!e.hidden)any=true;e=e.nextElementSibling;}
    h.hidden=!any;
  });
  chips.forEach(function(ch){ch.classList.toggle('off',!on[ch.getAttribute('data-cat')]);});
  shown.textContent=count+' of '+cards.length+' shown';
  clear.hidden=!q.value;
  empty.hidden=count>0||cards.length===0;
}
chips.forEach(function(ch){ch.addEventListener('click',function(){var c=ch.getAttribute('data-cat');on[c]=!on[c];apply();});});
Array.prototype.slice.call(document.querySelectorAll('[data-preset]')).forEach(function(a){a.addEventListener('click',function(ev){ev.preventDefault();var f=presets[a.getAttribute('data-preset')];chips.forEach(function(ch){var c=ch.getAttribute('data-cat');on[c]=f(c);});apply();});});
q.addEventListener('input',apply);
q.addEventListener('keydown',function(e){if(e.key==='Escape'){q.value='';apply();}});
clear.addEventListener('click',function(){q.value='';apply();q.focus();});
var init=new URLSearchParams(location.search).get('q');if(init)q.value=init;
apply();
})();
""";

    private static string H(string s) => System.Net.WebUtility.HtmlEncode(s);

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
