using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Managed Excel access. Reads use forward XML streams; writes preserve workbook content.</summary>
public sealed class ExcelStore
{
    public string LastReadProfile { get; private set; } = "";
    /// <summary>Automatic pre-write Excel snapshots; configured by WarehouseService.</summary>
    public string BackupDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HISA Kho Vat Tu", "Backups", "Excel");
    private const string ContSheet = "Cont đi Hưng Yên";
    private readonly object gate = new();
    private readonly Dictionary<string, (string Stamp, List<Package> Packages, List<string> Warnings)> sourceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Stamp, List<ShipmentRecord> Rows)> shipmentCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class CellData
    {
        public string Text { get; init; } = "";
        public double? Number { get; init; }
        public string Format { get; init; } = "";
        public string Day { get; init; } = "";
    }
    private sealed class SheetData
    {
        public string Name { get; init; } = "";
        public SortedDictionary<int, Dictionary<int, CellData>> Rows { get; } = [];
        public string Text(int row, int col) => Rows.TryGetValue(row, out var values) && values.TryGetValue(col, out var cell) ? cell.Text : "";
        public string Day(int row, int col)
        {
            if (!Rows.TryGetValue(row,out var values) || !values.TryGetValue(col,out var cell)) return "";
            if (cell.Day.Length > 0) return cell.Day;
            if (DateTime.TryParseExact(cell.Text, ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return day.ToString("yyyy-MM-dd");
            return "";
        }
        public decimal? Quantity(int row, int col)
        {
            if (!Rows.TryGetValue(row,out var values) || !values.TryGetValue(col,out var cell)) return null;
            if (cell.Number.HasValue) return (decimal)cell.Number.Value;
            return QuantityNumbers.TryParse(cell.Text,out var quantity)?quantity:null;
        }
    }


    private static string Stamp(string path)
    {
        var file = new FileInfo(path);
        return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    }

    private static List<SheetData> ReadSheets(string path,HashSet<string>? includedSheets=null,int maxColumn=int.MaxValue,bool firstOrExportSheet=false)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(file,ZipArchiveMode.Read);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var shared = new List<string>();
        var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry != null)
        {
            using var stream = sharedEntry.Open();
            using var reader = XmlReader.Create(stream,new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si") continue;
                using var subtree = reader.ReadSubtree();
                var item = XElement.Load(subtree);
                shared.Add(string.Concat(item.Descendants(ns + "t").Select(x=>x.Value)));
            }
        }
        var formats = new Dictionary<int,string>();
        var styleFormats = new List<(string Format,bool Date,string Zero)>();
        var stylesEntry = zip.GetEntry("xl/styles.xml");
        if (stylesEntry != null)
        {
            using var stream = stylesEntry.Open();
            var styles = XDocument.Load(stream);
            foreach (var format in styles.Descendants(ns+"numFmt"))
                if (int.TryParse((string?)format.Attribute("numFmtId"),out var id)) formats[id] = (string?)format.Attribute("formatCode") ?? "";
            foreach (var xf in styles.Root?.Element(ns+"cellXfs")?.Elements(ns+"xf") ?? [])
            {
                var id = (int?)xf.Attribute("numFmtId") ?? 0;
                var format=formats.GetValueOrDefault(id,"");
                var simple=format.Split(';')[0];
                var dateTokens=Regex.Replace(Regex.Replace(simple,"\"[^\"]*\"", ""),@"\[[^\]]*\]|\\.","");
                var dateFormat=id is >=14 and <=22 or >=27 and <=36 or >=45 and <=47 or >=50 and <=58 || Regex.IsMatch(dateTokens,@"[dy]",RegexOptions.IgnoreCase);
                styleFormats.Add((format,dateFormat,Regex.IsMatch(simple,@"^0{2,}$")?simple:""));
            }
        }
        using var wbStream = (zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Tệp không có workbook Excel.")).Open();
        var workbook = XDocument.Load(wbStream);
        var date1904 = (string?)workbook.Root?.Element(ns+"workbookPr")?.Attribute("date1904") is "1" or "true";
        using var relStream = (zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("Không có liên kết trang tính.")).Open();
        var relationships = XDocument.Load(relStream).Root!.Elements().ToDictionary(x=>(string)x.Attribute("Id")!,x=>(string)x.Attribute("Target")!);
        var output = new List<SheetData>();
        var preferredSheet=firstOrExportSheet?(workbook.Descendants(ns+"sheet").FirstOrDefault(x=>Rules.Code((string?)x.Attribute("name"))=="XUATKHO")??workbook.Descendants(ns+"sheet").FirstOrDefault()):null;
        foreach (var sheet in workbook.Descendants(ns+"sheet"))
        {
            if(firstOrExportSheet&&sheet!=preferredSheet)continue;
            var sheetName=(string?)sheet.Attribute("name")??"";
            if(includedSheets!=null&&!includedSheets.Contains(Rules.Code(sheetName)))continue;
            var id = (string?)sheet.Attribute(rel+"id") ?? "";
            if (!relationships.TryGetValue(id,out var target)) continue;
            var entryPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/"+target;
            entryPath = entryPath.Replace("\\","/").Replace("xl/../","");
            var entry = zip.GetEntry(entryPath);
            if (entry == null) continue;
            var data = new SheetData { Name = sheetName };
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream,new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit });
            int rowNumber=0,nextColumn=1;
            Dictionary<int,CellData>? cells=null;
            while (reader.Read())
            {
                if(reader.NodeType==XmlNodeType.Element&&reader.LocalName=="row")
                {
                    rowNumber=int.TryParse(reader.GetAttribute("r"),out var rn)?rn:rowNumber+1;
                    cells=new Dictionary<int,CellData>();nextColumn=1;
                    continue;
                }
                if(reader.NodeType==XmlNodeType.EndElement&&reader.LocalName=="row")
                {
                    if(cells?.Count>0)data.Rows[rowNumber]=cells;
                    cells=null;continue;
                }
                if(reader.NodeType!=XmlNodeType.Element||reader.LocalName!="c"||cells==null)continue;
                var reference=reader.GetAttribute("r")??"";
                var rawType=reader.GetAttribute("t")??"";
                var rawStyle=reader.GetAttribute("s");
                using(var subtree=reader.ReadSubtree())
                {
                    var column = 0;
                    foreach (var ch in reference.TakeWhile(char.IsLetter)) column = column*26+char.ToUpperInvariant(ch)-'A'+1;
                    if (column == 0) column = nextColumn;
                    nextColumn = column+1;
                    if(column>maxColumn)continue;
                    var cell=XElement.Load(subtree);
                    var type = rawType;
                    var raw = cell.Element(ns+"v")?.Value ?? "";
                    if(raw.Length==0&&type!="inlineStr")continue;
                    var style = int.TryParse(rawStyle,out var si)?si:0;
                    var format = style < styleFormats.Count ? styleFormats[style] : ("",false,"");
                    string text = raw, day = "";
                    double? number = null;
                    if (type == "s" && int.TryParse(raw,out var index)) text = index >= 0 && index < shared.Count ? shared[index] : "";
                    else if (type == "inlineStr") text = string.Concat(cell.Element(ns+"is")?.Descendants(ns+"t").Select(x=>x.Value) ?? []);
                    else if (type == "d" && DateTime.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.None,out var iso)) { day = iso.ToString("yyyy-MM-dd"); text = day; }
                    else if (type is not ("str" or "e" or "b") && double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))
                    {
                        number = value;
                        text = value.ToString("0.################",CultureInfo.InvariantCulture);
                        if (format.Item3.Length>0 && value == Math.Truncate(value)) text = value.ToString(format.Item3,CultureInfo.InvariantCulture);
                        if (format.Item2 && value is >0 and <2958466)
                        {
                            try { day = (date1904?new DateTime(1904,1,1).AddDays(value):DateTime.FromOADate(value)).ToString("yyyy-MM-dd"); text = day; } catch (ArgumentException) { }
                        }
                    }
                    if(text.Length>0)cells[column] = new CellData { Text = text.Trim(), Number = number, Day = day, Format = format.Item1 };
                }
            }
            output.Add(data);
        }
        return output;
    }

    public WorkbookSnapshot ReadMaster(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy Tổng hợp.xlsx.",path);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var sheets = ReadSheets(path,[Rules.Code(ContSheet),"CHITIET"],29);
        var xmlMs=timer.ElapsedMilliseconds;
        var cont = sheets.FirstOrDefault(x=>Rules.Code(x.Name)==Rules.Code(ContSheet));
        var detail = sheets.FirstOrDefault(x=>Rules.Code(x.Name)=="CHITIET");
        var snapshot = new WorkbookSnapshot();
        if (cont != null) snapshot.ContPackages = ReadPackages(cont,path,false);
        else snapshot.Warnings.Add("Tổng hợp.xlsx chưa có trang Cont đi Hưng Yên.");
        if (detail != null) snapshot.DetailPackages = ReadPackages(detail,path,true);
        else snapshot.Warnings.Add("Tổng hợp.xlsx chưa có trang chi tiết để theo dõi ngày cont về.");
        var packageMs=timer.ElapsedMilliseconds-xmlMs;
        snapshot.Packages = MergePackages(snapshot.ContPackages,snapshot.DetailPackages);
        LastReadProfile=$"XML {xmlMs}ms; package grouping {packageMs}ms; merging {timer.ElapsedMilliseconds-xmlMs-packageMs}ms";
        return snapshot;
    }

    private static List<Package> ReadPackages(SheetData sheet,string path,bool detail)
    {
        var output = new Dictionary<string,Package>();
        var ordinals = new Dictionary<string,int>();
        var inHy = !detail;
        var block=0;
        foreach (var row in sheet.Rows.Keys.Where(x=>x >= (detail?3:2)))
        {
            var offset = detail?1:0;
            var marker = sheet.Text(row,5+offset);
            if (marker.Length>0)
            {
                inHy = Rules.Code(marker).Contains("HUNGYEN");
                if(inHy)block++;
            }
            if (!inHy) continue;
            var contract = Rules.CleanContract(sheet.Text(row,4+offset));
            var material = sheet.Text(row,6+offset); var unit = sheet.Text(row,7+offset);
            if (contract.Length==0 || material.Length==0 || unit.Length==0) continue;
            var day = sheet.Day(row,1+offset);
            var departureDay = detail && sheet.Day(row,29).Length>0
                ? sheet.Day(row,29) : day;
            var project = sheet.Text(row,2+offset); var company = sheet.Text(row,3+offset);
            var identity = Rules.Identity(contract,project,company);
            var baseKey = identity+"|"+day;
            var key = baseKey+"|block:"+block;
            if (!output.TryGetValue(key,out var package))
            {
                var ordinal=ordinals.GetValueOrDefault(baseKey)+1;ordinals[baseKey]=ordinal;
                package = new Package { Id=sheet.Name+"|"+baseKey+"|"+ordinal, Contract=contract, Project=project, Company=company, SourceDate=departureDay, ArrivalDate=detail?day:"", ContDate=departureDay, SourceFile=Path.GetFileName(path),SourcePath=path,SourceSheet=sheet.Name,PackageNo=ordinal,SourceRowStart=row,SourceRowEnd=row,
                    ContainerNumber=detail?sheet.Text(row,27):"",OriginPackageId=detail?sheet.Text(row,28):"" };
                output[key] = package;
            }
            package.SourceRowEnd = row;
            package.Items.Add(NewLine(package,material,unit,sheet.Quantity(row,8+offset),sheet.Text(row,9+offset)));
        }
        return output.Values.ToList();
    }

    private static MaterialLine NewLine(Package p,string material,string unit,decimal? quantity,string note="") => new()
    {
        Id = p.Id+"|"+p.Items.Count, Contract=p.Contract,Project=p.Project,Company=p.Company,Material=material,Unit=unit,Quantity=quantity,Category=Rules.Classify(material),Note=note,PackageId=p.Id,PackageNo=p.PackageNo,SourceDate=p.SourceDate,ArrivalDate=p.ArrivalDate,SourceFile=p.SourceFile,IsHungYen=true,Include=true
    };
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value),JsonOptions)!;
    private static MaterialLine CopyLine(MaterialLine x)=>new()
    {
        Id=x.Id,Contract=x.Contract,Project=x.Project,Company=x.Company,Material=x.Material,Unit=x.Unit,Quantity=x.Quantity,Category=x.Category,Note=x.Note,Include=x.Include,IsHungYen=x.IsHungYen,PackageId=x.PackageId,Package=x.Package,PackageNo=x.PackageNo,SourceDate=x.SourceDate,ArrivalDate=x.ArrivalDate,SourceFile=x.SourceFile
    };
    private static Package CopyPackage(Package x)=>new()
    {
        Id=x.Id,Contract=x.Contract,Project=x.Project,Company=x.Company,SourceDate=x.SourceDate,ArrivalDate=x.ArrivalDate,ContDate=x.ContDate,ContainerNumber=x.ContainerNumber,OriginPackageId=x.OriginPackageId,SourceFile=x.SourceFile,SourcePath=x.SourcePath,SourceSheet=x.SourceSheet,SourceRowStart=x.SourceRowStart,SourceRowEnd=x.SourceRowEnd,PackageNo=x.PackageNo,Items=x.Items.Select(CopyLine).ToList()
    };
    private static string ItemKey(MaterialLine line) => Rules.Code(line.Material)+"|"+Rules.Code(line.Unit)+"|"+(line.Quantity?.ToString("0.################",CultureInfo.InvariantCulture) ?? "");
    private static Dictionary<string,int> ItemCounts(IEnumerable<MaterialLine> lines) => lines.GroupBy(ItemKey).ToDictionary(x=>x.Key,x=>x.Count());
    private static int Overlap(Package left,Package right)
    {
        var a = ItemCounts(left.Items); var b = ItemCounts(right.Items);
        return a.Sum(x=>Math.Min(x.Value,b.GetValueOrDefault(x.Key)));
    }
    private static List<Package> MergePackages(List<Package> cont,List<Package> detail)
    {
        var result = cont.Select(CopyPackage).ToList();
        // Index only the original cont packages. Appended arrival-only packages must
        // never become candidates, and scanning every appended row was quadratic.
        var candidates=result.GroupBy(x=>x.Identity).ToDictionary(x=>x.Key,x=>x.ToList());
        var claimed = new HashSet<string>();
        foreach (var source in detail)
        {
            var sameIdentity=candidates.GetValueOrDefault(source.Identity,[]);
            // New metadata matches exactly one physical source package even across identical contracts.
            var target = source.OriginPackageId.Length>0
                ? sameIdentity.FirstOrDefault(x=>x.Id==source.OriginPackageId && !claimed.Contains(x.Id))
                : sameIdentity.Where(x=>!claimed.Contains(x.Id))
                    .OrderByDescending(x=>Overlap(x,source)).ThenByDescending(x=>x.PackageNo==source.PackageNo)
                    .ThenByDescending(x=>x.SourceDate==source.SourceDate).FirstOrDefault();
            if (target == null) { result.Add(CopyPackage(source)); continue; }
            claimed.Add(target.Id);
            target.ArrivalDate = source.ArrivalDate;
            target.ContainerNumber = source.ContainerNumber;
            target.OriginPackageId = source.OriginPackageId;
            // The arrival sheet is the authoritative received list (including KT corrections).
            target.Items = source.Items.Select(CopyLine).ToList();
            foreach (var line in target.Items) { line.PackageId=target.Id;line.PackageNo=target.PackageNo;line.SourceDate=target.SourceDate;line.ArrivalDate=source.ArrivalDate; }
        }
        return result.OrderBy(x=>x.SourceDate).ThenBy(x=>x.Contract).ThenBy(x=>x.Project).ThenBy(x=>x.PackageNo).ToList();
    }

    private sealed record Metadata(string Contract,string Project,string Company,string Day="",int Year=0);
    private static List<Metadata> ScheduleMetadata(IEnumerable<string> parents,List<string> warnings)
    {
        var result = new List<Metadata>();
        foreach (var parent in parents.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        foreach (var path in Directory.EnumerateFiles(parent,"*.xlsx").Where(x=>Path.GetFileName(x).StartsWith("Lịch xuất hàng",StringComparison.OrdinalIgnoreCase)||Rules.Code(Path.GetFileNameWithoutExtension(x))=="HANGTHEOCONT"))
        {
            try
            {
                foreach (var sheet in ReadSheets(path)) foreach (var row in sheet.Rows.Keys)
                {
                    var contract = Rules.CleanContract(sheet.Text(row,5));
                    if (!Regex.IsMatch(contract,@"\d{4}")) continue;
                    var project = sheet.Text(row,3); var company = sheet.Text(row,4);
                    if (project.Length>0) result.Add(new Metadata(contract,project,company));
                }
            }
            catch (Exception ex) { warnings.Add($"Chưa đọc được bảng đối chiếu {Path.GetFileName(path)}: {ex.Message}"); }
        }
        return result;
    }
    private static List<Metadata> ManifestMetadata(IEnumerable<string> manifests,List<string> warnings)
    {
        var result = new List<Metadata>();
        foreach (var path in manifests)
        {
            try
            {
                foreach (var sheet in ReadSheets(path))
                {
                    var day = ""; var destination = ""; var year=DateTime.Today.Year;
                    foreach (var row in sheet.Rows.Keys)
                    {
                        var texts = Enumerable.Range(1,6).Select(col=>sheet.Text(row,col)).ToList();
                        foreach (var text in texts)
                        {
                            var match = Regex.Match(Rules.Norm(text),@"NGAY XUAT HANG:\s*(\d{1,2})/(\d{1,2})/(\d{4})");
                            if (match.Success && DateTime.TryParseExact($"{match.Groups[1].Value}/{match.Groups[2].Value}/{match.Groups[3].Value}","d/M/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)) { day=date.ToString("yyyy-MM-dd");year=date.Year; }
                            if (Rules.Norm(text).Contains("CONT")) destination=text;
                        }
                        if (day.Length==0 || !Rules.Code(destination).Contains("HUNGYEN")) continue;
                        if (int.TryParse(sheet.Text(row,1),out _) && sheet.Text(row,2).Length>0 && sheet.Text(row,4).Length>0)
                            result.Add(new Metadata(Rules.CleanContract(sheet.Text(row,4)),sheet.Text(row,2),sheet.Text(row,3),day,year));
                        else if (sheet.Text(row,2).StartsWith("BG ",StringComparison.OrdinalIgnoreCase)) result.Add(new Metadata(Rules.CleanContract(sheet.Text(row,2)),sheet.Text(row,2),sheet.Text(row,3),day,year));
                    }
                }
            }
            catch (Exception ex) { warnings.Add($"Chưa đọc được danh mục cont {Path.GetFileName(path)}: {ex.Message}"); }
        }
        return result;
    }
    private static string InferContract(string stem)
    {
        var cleaned = Regex.Replace(stem,@"(?:[-_]\d+|\(\s*(?:P\d+|\d+)\s*\))$","",RegexOptions.IgnoreCase).Trim();
        var bg = Regex.Match(cleaned,@"^BG[\s._:-]*(\d+)(?:\.(\d+))?",RegexOptions.IgnoreCase);
        if (bg.Success)
        {
            var bgDigits=bg.Groups[1].Value;
            if(bg.Groups[2].Success)return Rules.CleanContract(bgDigits+"."+bg.Groups[2].Value);
            // Compact BG filenames encode decimal codes such as BG47011026 → 470110.26.
            if(bgDigits.Length>=6&&bgDigits.EndsWith("26"))return bgDigits[..^2]+".26";
            return bgDigits;
        }
        var match = Regex.Match(cleaned,@"(\d{3,8}(?:202[4-9]))(?:\D|$)");
        if (match.Success) return match.Groups[1].Value.Length>8?match.Groups[1].Value[^8..]:match.Groups[1].Value;
        var digits = Regex.Replace(cleaned,@"\D","");
        return digits.Length>=8?digits[^8..]:digits;
    }
    private static string FileDay(string path)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(path));
        var m = Regex.Match(parent??"",@"(?<!\d)(\d{1,2})[.\-/](\d{1,2})(?:[.\-/](\d{4}))?(?!\d)");
        if (m.Success)
        {
            var year=m.Groups[3].Success?int.Parse(m.Groups[3].Value):DateTime.Today.Year;
            try { return new DateTime(year,int.Parse(m.Groups[2].Value),int.Parse(m.Groups[1].Value)).ToString("yyyy-MM-dd"); } catch (ArgumentOutOfRangeException) { }
        }
        return "";
    }
    private static Metadata? MatchMetadata(string path,List<Metadata> manifests,List<Metadata> schedule)
    {
        var stem=Path.GetFileNameWithoutExtension(path); var inferred=InferContract(stem); var day=FileDay(path);
        var matches=manifests.Where(x=>(day.Length==0||x.Day==day) && Rules.Contract(x.Contract)==Rules.Contract(inferred)).ToList();
        if (matches.Select(x=>Rules.Identity(x.Contract,x.Project,x.Company)).Distinct().Count()==1) return matches.First();
        var suffix=Regex.Match(stem,@"\((\d+)\)$");
        if (suffix.Success)
        {
            var tagged=matches.Where(x=>x.Project.Trim().EndsWith(suffix.Value)).ToList();
            if (tagged.Count==1) return tagged[0];
        }
        // A few supplier filenames contain a mistyped contract number. The dated
        // HY manifest is authoritative only when the filename's company has one
        // unambiguous contract/project on that day; never use fuzzy number matching.
        var stemCode=Rules.Code(stem);
        var companyMatches=manifests.Where(x=>(day.Length==0||x.Day==day)&&Rules.Code(x.Company).Length>=5&&stemCode.Contains(Rules.Code(x.Company))).ToList();
        if(companyMatches.Select(x=>Rules.Identity(x.Contract,x.Project,x.Company)).Distinct().Count()==1)return companyMatches.First();
        return null;
    }
    public SourceScanResult ScanSources(AppSettings settings,IProgress<OperationProgress>? progress=null,CancellationToken cancellationToken=default,bool includeReceivedArchives=true)
    {
        lock (gate)
        {
            var result=new SourceScanResult();
            var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parents=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in settings.DataSources)
            {
                if (Directory.Exists(source))
                {
                    try { foreach(var path in Directory.EnumerateFiles(source,"*.xlsx",SearchOption.AllDirectories)) if(!Path.GetFileName(path).StartsWith("~$") && (includeReceivedArchives || !ContFileOrganizer.IsAlreadySorted(path,source))) files.Add(Path.GetFullPath(path)); }
                    catch(Exception ex) { result.Warnings.Add($"Không quét đủ nguồn {source}: {ex.Message}"); }
                    // Received originals live beside CONT ĐI, never inside it. Include
                    // them in future rebuilds so stable source IDs remain discoverable.
                    var receivedFolder=ContFileOrganizer.SiblingFolder(source,"Đã nhập kho");
                    if(includeReceivedArchives && Directory.Exists(receivedFolder))
                    {
                        try
                        {
                            foreach(var path in Directory.EnumerateFiles(receivedFolder,"*.xlsx",SearchOption.AllDirectories))
                                if(!Path.GetFileName(path).StartsWith("~$"))files.Add(Path.GetFullPath(path));
                        }
                        catch(Exception ex){result.Warnings.Add("Không quét đủ Đã nhập kho: "+ex.Message);}
                    }
                    parents.Add(Directory.GetParent(source)?.FullName??source);
                }
                else if (File.Exists(source)) { files.Add(Path.GetFullPath(source));parents.Add(Path.GetDirectoryName(source)!); }
                else result.Warnings.Add($"Không tìm thấy nguồn: {source}");
            }
            // Files named hanggui/Hàng gửi are sorted separately and never treated as stock.
            var roots=settings.DataSources.Where(Directory.Exists).ToList();
            foreach(var file in files.Where(ContFileOrganizer.HasHangGuiName))
            {
                var candidate=ContFileOrganizer.MakeCandidate(file,roots);
                if(candidate!=null)result.HangGuiFiles.Add(candidate);
            }
            var manifests=files.Where(x=>!ContFileOrganizer.HasHangGuiName(x)&&Path.GetFileName(x).StartsWith("THANG",StringComparison.OrdinalIgnoreCase)).ToList();
            var manifest=ManifestMetadata(manifests,result.Warnings);
            var metadata=ScheduleMetadata(parents,result.Warnings);
            var raw=files.Where(x=>!Path.GetFileName(x).StartsWith("~$") && !ContFileOrganizer.HasHangGuiName(x)
                && !ContFileOrganizer.IsHangGuiFolder(x) && !manifests.Contains(x) && !IsReserved(x,settings)).Order().ToList();
            result.FileCount=raw.Count;
            var metadataStamp=string.Join("|",manifest.Select(x=>x.ToString()).Concat(metadata.Select(x=>x.ToString())))+"|"+string.Join("|",roots);
            for(int index=0;index<raw.Count;index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path=raw[index];
                progress?.Report(new OperationProgress { Message=$"Đang quét {Path.GetFileName(path)}",Current=index+1,Total=raw.Count });
                try
                {
                    var stamp=Stamp(path)+metadataStamp;
                    if(sourceCache.TryGetValue(path,out var cached) && cached.Stamp==stamp)
                    {
                        result.Packages.AddRange(Clone(cached.Packages));result.Warnings.AddRange(cached.Warnings);
                        if(cached.Packages.Count>0 && cached.Warnings.Count==0)
                        {
                            var classified=ContFileOrganizer.MakeCandidate(path,roots);
                            if(classified!=null)result.ImportedHungYenFiles.Add(classified);
                        }
                        continue;
                    }
                    var fileWarnings=new List<string>();
                    var packages=ParseSource(path,manifest,metadata,fileWarnings,roots,requireHungYenMarker:!includeReceivedArchives);
                    sourceCache[path]=(stamp,packages,fileWarnings);
                    result.Warnings.AddRange(fileWarnings);
                    result.Packages.AddRange(Clone(packages));
                    if(packages.Count>0 && fileWarnings.Count==0)
                    {
                        var classified=ContFileOrganizer.MakeCandidate(path,roots);
                        if(classified!=null)result.ImportedHungYenFiles.Add(classified);
                    }
                    result.ChangedFiles++;
                }
                catch(Exception ex) when(ex is not OperationCanceledException)
                {
                    result.Warnings.Add($"Chưa đọc được {Path.GetFileName(path)}: {ex.Message}");
                    if(sourceCache.TryGetValue(path,out var old)) result.Packages.AddRange(Clone(old.Packages));
                }
            }
            return result;
        }
    }
    private static bool IsReserved(string path,AppSettings settings)
    {
        // Never mistake a production equipment catalogue for a container file,
        // including when an older installation has stored it under CONT ĐI.
        if(EquipmentListReader.IsConfiguredEquipmentFile(path,settings))return true;
        var name=Rules.Code(Path.GetFileNameWithoutExtension(path));
        return Path.GetFullPath(path).Equals(settings.MasterPath,StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).Equals(settings.KtPath,StringComparison.OrdinalIgnoreCase) || name is "TONGHOP" or "KTTONGHOP" or "HANGTHEOCONT" or "THANHPHAMXUATKHO" || name.StartsWith("LICHXUATHANG") || name.StartsWith("SOTHEODOIXUATHANG") || name.StartsWith("PHIEUXUATVATTU") || Path.GetDirectoryName(path)!.Split(Path.DirectorySeparatorChar).Any(x=>Rules.Code(x) is "XUATHANG" or "BACKUPS" or "HISABACKUPS" or "NATIVEDATA");
    }
    private static List<Package> ParseSource(string path,List<Metadata> manifests,List<Metadata> schedule,List<string> warnings,IEnumerable<string> roots,bool requireHungYenMarker=false)
    {
        var stem=Path.GetFileNameWithoutExtension(path);var inferred=InferContract(stem);
        var match=MatchMetadata(path,manifests,schedule);
        var sameCode=schedule.Where(x=>Rules.Contract(x.Contract)==Rules.Contract(match?.Contract??inferred)).ToList();
        var suffix=Regex.Match(stem,@"\((\d+)\)$").Value;
        var sourceDay=FileDay(path);
        var manifestAmbiguous=manifests.Where(x=>(sourceDay.Length==0||x.Day==sourceDay)&&Rules.Contract(x.Contract)==Rules.Contract(inferred)).Select(x=>Rules.Identity(x.Contract,x.Project,x.Company)).Distinct().Count()>1;
        var possible=sameCode.DistinctBy(x=>Rules.Identity(x.Contract,x.Project,x.Company)).ToList();
        var tagged=possible.Where(x=>suffix.Length>0&&x.Project.Trim().EndsWith(suffix)).ToList();
        var meta=match??(manifestAmbiguous?null:tagged.Count==1?tagged[0]:possible.Count==1?possible[0]:null);
        var ambiguousProject=meta==null&&(possible.Count>1||manifestAmbiguous);
        var contract=match?.Contract??inferred;
        if(contract.Length==0) { warnings.Add($"Chưa nhận diện được số hợp đồng trong {Path.GetFileName(path)}");return []; }
        var project=meta?.Project??("Chưa đối chiếu công trình"+(suffix.Length>0?" "+suffix:""));
        if(match==null && suffix.Length>0 && !project.Trim().EndsWith(suffix)) project+=" "+suffix;
        var company=meta?.Company??"";
        var fuller=sameCode.Where(x=>Rules.Code(x.Project).Contains(Rules.Code(project)) && x.Project.Length>project.Length&&Rules.Code(x.Company)==Rules.Code(company)).DistinctBy(x=>Rules.Identity(x.Contract,x.Project,x.Company)).ToList();
        if(fuller.Count==1&&!sameCode.Any(x=>Rules.Code(x.Project)==Rules.Code(project)&&Rules.Code(x.Company)==Rules.Code(company)))project=fuller[0].Project;
        var day=FileDay(path);if(day.Length==0) day=match?.Day??"";
        var blocks=new List<Package>(); var pending=new List<MaterialLine>();var explicitDestination=false;int ordinal=0;
        foreach(var sheet in ReadSheets(path))
        {
            var header=sheet.Rows.Keys.FirstOrDefault(row=>Enumerable.Range(1,6).Any(c=>Rules.Code(sheet.Text(row,c)).Contains("TENVATTU")) && Enumerable.Range(1,6).Any(c=>Rules.Code(sheet.Text(row,c)) is "DVT" or "DONVITINH"));
            if(header==0) continue;
            Package? current=null;
            foreach(var row in sheet.Rows.Keys.Where(x=>x>header))
            {
                var marker=sheet.Text(row,1);
                if(marker.Length>0)
                {
                    var markerCode=Rules.Code(marker);
                    var hy=markerCode=="HUNGYEN";
                    // A textual destination always takes precedence over a manifest
                    // fallback; generic numeric/package markers are not destinations.
                    if(hy || !Regex.IsMatch(markerCode,@"^(?:(?:KIEN(?:HANG)?|PACKAGE|PKG|K))?\d+$")) explicitDestination=true;
                    current=null;
                    if(hy)
                    {
                        ordinal++;
                        current=new Package {Id="source|"+ContFileOrganizer.OriginalPath(path,roots)+"|"+ordinal,Contract=contract,Project=project,Company=company,SourceDate=day,ContDate=day,SourceFile=Path.GetFileName(path),SourcePath=path,SourceSheet=sheet.Name,PackageNo=ordinal,SourceRowStart=row,SourceRowEnd=row};
                        blocks.Add(current);
                    }
                }
                var material=sheet.Text(row,2);var unit=sheet.Text(row,3);
                if(material.Length==0||unit.Length==0||Rules.Code(material)=="TENVATTU") continue;
                if(current!=null) { current.SourceRowEnd=row;current.Items.Add(NewLine(current,material,unit,sheet.Quantity(row,4),sheet.Text(row,5))); }
                else pending.Add(new MaterialLine { Material=material,Unit=unit,Quantity=sheet.Quantity(row,4),Note=sheet.Text(row,5) });
            }
        }
        if(!requireHungYenMarker && !explicitDestination && blocks.Count==0 && match!=null && pending.Count>0)
        {
            var package=new Package {Id="source|"+ContFileOrganizer.OriginalPath(path,roots)+"|1",Contract=contract,Project=project,Company=company,SourceDate=day,ContDate=day,SourceFile=Path.GetFileName(path),SourcePath=path,PackageNo=1};
            foreach(var line in pending) package.Items.Add(NewLine(package,line.Material,line.Unit,line.Quantity,line.Note));
            blocks.Add(package);
        }
        var result=blocks.Where(x=>x.Items.Count>0).ToList();
        if(result.Count>0&&ambiguousProject)warnings.Add($"HĐ {inferred} trong {Path.GetFileName(path)} có nhiều công trình / công ty trong bảng đối chiếu; chưa chọn được công trình duy nhất. Hãy đối chiếu tên file hoặc hậu tố (1), (2) trước khi cập nhật tổng hợp.");
        foreach(var package in result)foreach(var line in package.Items.Where(x=>x.Quantity is null||x.Quantity<0))
            warnings.Add($"HĐ {contract} trong {Path.GetFileName(path)}: số lượng vật tư '{line.Material}' bị trống hoặc không hợp lệ. Giữ nguyên bảng tổng hợp cho đến khi nguồn được sửa.");
        return result;
    }

    private static void SetContract(IXLCell cell,string contract)
    {
        contract=Rules.CleanContract(contract);
        if(Regex.IsMatch(contract,@"^\d+(?:\.\d+)?$") && Regex.Replace(contract,@"\D","").Length<=15 && double.TryParse(contract,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var number))
        {
            cell.Value=number;
            cell.Style.NumberFormat.Format=contract.StartsWith('0') && !contract.Contains('.')?new string('0',contract.Length):contract.Contains('.')?"0."+new string('0',contract.Split('.')[1].Length):"0";
        }
        else { cell.Value=contract;cell.Style.NumberFormat.Format="@"; } // Preserve text/suffix/long numeric identifiers without Excel precision loss.
    }
    private static void SetDate(IXLCell cell,string day)
    {
        if(DateTime.TryParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)) cell.Value=date;
        else cell.Value=day;
        cell.Style.NumberFormat.Format="dd/MM/yyyy";
    }
    private void AtomicSave(XLWorkbook workbook,string path)
    {
        var directory=Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp=Path.Combine(directory,"."+Path.GetFileNameWithoutExtension(path)+"."+Guid.NewGuid().ToString("N")+".tmp.xlsx");
        try
        {
            if(File.Exists(path))
            {
                // Test write access before doing any expensive serialization or changing the original.
                using(var check=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
                // Keep pre-write copies in Documents, separate from editable Excel sources.
                Directory.CreateDirectory(BackupDirectory);
                var backup=Path.Combine(BackupDirectory,
                    Path.GetFileNameWithoutExtension(path)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..8]+".xlsx");
                File.Copy(path,backup,false);
            }
            workbook.SaveAs(temp,new ClosedXML.Excel.SaveOptions { EvaluateFormulasBeforeSaving=false,GenerateCalculationChain=false });
            File.Move(temp,path,true);
        }
        catch(IOException ex) { throw new IOException($"Không ghi được {Path.GetFileName(path)}. Hãy lưu và đóng tệp trong Excel rồi thử lại. {ex.Message}",ex); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    private static void StyleHeader(IXLRange range)
    {
        range.Style.Fill.BackgroundColor=XLColor.FromHtml("#173B5E");range.Style.Font.FontColor=XLColor.White;range.Style.Font.Bold=true;range.Style.Font.FontName="Calibri";range.Style.Alignment.WrapText=true;range.Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
    }
    public void WriteContSummary(string masterPath,IReadOnlyList<Package> packages)
    {
        lock(gate)
        {
            // An empty scan must never clear the user's existing data.
            if(packages.Count==0) return;
            using var wb=new XLWorkbook(masterPath);
            var ws=wb.Worksheets.FirstOrDefault(x=>x.Name==ContSheet)??wb.AddWorksheet(ContSheet);
            foreach(var merge in ws.MergedRanges.ToList()) merge.Unmerge();
            foreach(var table in ws.Tables.ToList()) ws.Tables.Remove(table.Name);
            ws.Clear();
            string[] headers=["Ngày cont đi","Tên công trình","Tên công ty","Số hợp đồng","Kiện hàng","Tên vật tư","Đơn vị tính","Số lượng","Ghi chú"];
            for(int col=0;col<headers.Length;col++) ws.Cell(1,col+1).Value=headers[col];
            int row=2;
            foreach(var package in packages.OrderBy(x=>x.SourceDate).ThenBy(x=>x.Contract).ThenBy(x=>x.Project).ThenBy(x=>x.PackageNo))
            {
                int start=row;
                foreach(var item in package.Items.Where(x=>x.IsHungYen))
                {
                    SetDate(ws.Cell(row,1),package.SourceDate);ws.Cell(row,2).Value=Rules.ProjectWithContDate(package.Project,package.SourceDate);ws.Cell(row,3).Value=package.Company;SetContract(ws.Cell(row,4),package.Contract);
                    if(row==start) ws.Cell(row,5).Value="HƯNG\nYÊN";
                    ws.Cell(row,6).Value=item.Material;ws.Cell(row,7).Value=item.Unit;
                    if(item.Quantity.HasValue) ws.Cell(row,8).Value=(double)item.Quantity.Value;
                    ws.Cell(row,9).Value=item.Note;row++;
                }
                if(row-start>1) ws.Range(start,5,row-1,5).Merge();
            }
            StyleHeader(ws.Range(1,1,1,9));
            ws.Row(1).Height=30;ws.SheetView.FreezeRows(1);ws.ShowGridLines=false;
            double[] widths=[15,48,24,17,12,52,13,14,24];for(int col=1;col<=9;col++) ws.Column(col).Width=widths[col-1];
            ws.Range(2,1,Math.Max(2,row-1),9).Style.Font.FontName="Times New Roman";
            ws.Range(2,8,Math.Max(2,row-1),8).Style.NumberFormat.Format="0.00";
            ws.Column(5).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;ws.Column(5).Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;ws.Column(5).Style.Alignment.WrapText=true;
            ws.Range(1,1,Math.Max(1,row-1),9).SetAutoFilter();
            if(wb.Worksheets.TryGetWorksheet("chi tiết",out var detail)) detail.Cell(2,2).Value="Ngày cont về";
            AtomicSave(wb,masterPath);
        }
    }

    private static List<Package> ReadKt(string path)
    {
        var sheets=ReadSheets(path);
        var sheet=sheets.FirstOrDefault(x=>Rules.Code(x.Name)=="CHITIET2")??sheets.FirstOrDefault(x=>Rules.Code(x.Name)=="CHITIET")??sheets.FirstOrDefault();
        return sheet==null?[]:ReadPackages(sheet,path,true);
    }
    public List<ComparisonRow> CompareWithKt(string ktPath,IReadOnlyList<Package> packages)
    {
        List<Package> kt;
        try { if(!File.Exists(ktPath)) throw new FileNotFoundException("Không tìm thấy KT tổng hợp.xlsx.");kt=ReadKt(ktPath); }
        catch(Exception ex) { return packages.GroupBy(x=>x.Identity).Select(g=>new ComparisonRow {Contract=g.First().Contract,Project=g.First().Project,Company=g.First().Company,Status="unavailable",Note=ex.Message,ContItems=g.Sum(x=>x.Items.Count),ContOnly=g.SelectMany(x=>x.Items).ToList()}).ToList(); }
        var groups=kt.GroupBy(x=>x.Identity).ToDictionary(x=>x.Key,x=>x.SelectMany(p=>p.Items).ToList());
        var result=new List<ComparisonRow>();
        foreach(var group in packages.GroupBy(x=>x.Identity))
        {
            var p=group.First();var lines=group.SelectMany(x=>x.Items).ToList();var row=new ComparisonRow { Contract=p.Contract,Project=p.Project,Company=p.Company,ContItems=lines.Count };
            if(!groups.TryGetValue(group.Key,out var other)) { row.Status="not_found";row.Note="Không có dữ liệu trùng số HĐ, công trình và công ty trong KT tổng hợp.xlsx.";row.ContOnly=lines;result.Add(row);continue; }
            row.KtItems=other.Count;var a=ItemCounts(lines);var b=ItemCounts(other);
            row.ContOnly=Difference(lines,a,b);row.KtOnly=Difference(other,b,a);
            row.Status=row.ContOnly.Count==0 && row.KtOnly.Count==0?"matched":"mismatch";
            row.Note=row.Status=="matched"?"Danh mục và số lượng khớp.":"Có chênh lệch danh mục, đơn vị hoặc số lượng. Xem các dòng chi tiết hai bên.";
            result.Add(row);
        }
        return result;
    }
    private static List<MaterialLine> Difference(List<MaterialLine> source,Dictionary<string,int> left,Dictionary<string,int> right)
    {
        var remaining=left.ToDictionary(x=>x.Key,x=>Math.Max(0,x.Value-right.GetValueOrDefault(x.Key)));
        var result=new List<MaterialLine>();
        foreach(var line in source) { var key=ItemKey(line);if(remaining.GetValueOrDefault(key)>0) {result.Add(Clone(line));remaining[key]--; } }
        return result;
    }
    private static void RemovePackageRows(IXLWorksheet sheet,IEnumerable<Package> packages)
    {
        var selected=packages.Where(x=>x.SourceRowStart>=3&&x.SourceRowEnd>=x.SourceRowStart).ToList();
        var rows=new HashSet<int>();
        foreach(var package in selected)
        {
            for(int row=package.SourceRowStart;row<=package.SourceRowEnd;row++)
            {
                var contract=sheet.Cell(row,5).DataType==XLDataType.Number?sheet.Cell(row,5).GetDouble().ToString("0.################",CultureInfo.InvariantCulture):sheet.Cell(row,5).GetString();
                var expectedDay=package.ArrivalDate.Length>0?package.ArrivalDate:package.SourceDate;
                if(row>=package.SourceRowStart&&row<=package.SourceRowEnd&&Rules.Identity(contract,sheet.Cell(row,3).GetString(),sheet.Cell(row,4).GetString())==package.Identity&&(expectedDay.Length==0||CellDay(sheet.Cell(row,2))==expectedDay))rows.Add(row);
            }
        }
        if(rows.Count==0)return;
        // Start/end ranges may overlap when a sheet interleaves two contracts.
        // Delete actual selected identity rows once, never the intervening project.
        var markerEdits=new Dictionary<int,string>();var inherited="";var kept="";
        var lastRow=sheet.LastRowUsed()?.RowNumber()??2;
        for(int row=3;row<=lastRow;row++)
        {
            var marker=sheet.Cell(row,6).GetString();if(marker.Length>0)inherited=marker;
            if(rows.Contains(row))continue;
            if(marker.Length>0)kept=marker;
            else if(inherited!=kept&&inherited.Length>0){markerEdits[row]=inherited;kept=inherited;}
        }
        var sorted=rows.Order().ToArray();
        bool Intersects(int first,int last)
        {
            var index=Array.BinarySearch(sorted,first);if(index<0)index=~index;
            return index<sorted.Length&&sorted[index]<=last;
        }
        foreach(var merge in sheet.MergedRanges.Where(x=>Intersects(x.RangeAddress.FirstAddress.RowNumber,x.RangeAddress.LastAddress.RowNumber)).ToList())merge.Unmerge();
        foreach(var edit in markerEdits)sheet.Cell(edit.Key,6).Value=edit.Value;
        var ranges=new List<(int First,int Last)>();var start=sorted[0];var end=start;
        foreach(var row in sorted.Skip(1)){if(row==end+1)end=row;else{ranges.Add((start,end));start=end=row;}}
        ranges.Add((start,end));
        foreach(var range in ranges.OrderByDescending(x=>x.First))sheet.Rows(range.First,range.Last).Delete();
    }
    private static string CellDay(IXLCell cell)
    {
        if(cell.DataType==XLDataType.DateTime)return cell.GetDateTime().ToString("yyyy-MM-dd");
        if(cell.DataType==XLDataType.Number)
        {
            try{return DateTime.FromOADate(cell.GetDouble()).ToString("yyyy-MM-dd");}catch(ArgumentException){return "";}
        }
        return DateTime.TryParseExact(cell.GetString(),["yyyy-MM-dd","dd/MM/yyyy","d/M/yyyy","dd-MM-yyyy","d-M-yyyy","dd.MM.yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date.ToString("yyyy-MM-dd"):"";
    }
    private static int AppendDetailPackage(IXLWorksheet sheet,Package package,string day)
    {
        var row=Math.Max(3,(sheet.LastRowUsed()?.RowNumber()??2)+1);var first=row;
        foreach(var line in package.Items.Where(x=>x.IsHungYen))
        {
            if(first>3) sheet.Row(row).Style=sheet.Row(3).Style;
            sheet.Cell(row,1).FormulaA1=$"E{row}";SetDate(sheet.Cell(row,2),day);sheet.Cell(row,3).Value=Rules.ProjectWithContDate(package.Project,package.SourceDate);sheet.Cell(row,4).Value=package.Company;SetContract(sheet.Cell(row,5),package.Contract);
            sheet.Cell(row,6).Value=row==first?"HƯNG YÊN":"";sheet.Cell(row,7).Value=line.Material;sheet.Cell(row,8).Value=line.Unit;if(line.Quantity.HasValue)sheet.Cell(row,9).Value=(double)line.Quantity.Value;sheet.Cell(row,10).Value=line.Note;
            if (row==first && (package.ContainerNumber.Length>0 || package.OriginPackageId.Length>0))
            {
                // Reserved, hidden metadata outside the user's visible template (A:J).
                var reservedA=sheet.Cell(2,27).GetString();
                var reservedB=sheet.Cell(2,28).GetString();
                var reservedC=sheet.Cell(2,29).GetString();
                if ((reservedA.Length>0 && reservedA!="HISA_CONTAINER") ||
                    (reservedB.Length>0 && reservedB!="HISA_SOURCE_PACKAGE") ||
                    (reservedC.Length>0 && reservedC!="HISA_CONT_DEPARTURE") ||
                    (reservedA.Length==0 && sheet.Column(27).CellsUsed().Any()) ||
                    (reservedB.Length==0 && sheet.Column(28).CellsUsed().Any()) ||
                    (reservedC.Length==0 && sheet.Column(29).CellsUsed().Any()))
                    throw new InvalidOperationException("Cột AA/AB trong Tổng hợp.xlsx có dữ liệu khác; không ghi đè. Liên hệ quản trị để chọn vị trí lưu ID kiện.");
                sheet.Cell(2,27).Value="HISA_CONTAINER";
                sheet.Cell(2,28).Value="HISA_SOURCE_PACKAGE";
                sheet.Cell(2,29).Value="HISA_CONT_DEPARTURE";
                sheet.Cell(row,27).Value=package.ContainerNumber;
                sheet.Cell(row,28).Value=package.OriginPackageId;
                sheet.Cell(row,29).Value=package.SourceDate;
                sheet.Column(27).Hide();
                sheet.Column(28).Hide();
                sheet.Column(29).Hide();
            }
            row++;
        }
        if(row-first>1) sheet.Range(first,6,row-1,6).Merge();
        if(row>first) { sheet.Range(first,6,row-1,6).Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;sheet.Range(first,6,row-1,6).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;sheet.Range(first,9,row-1,9).Style.NumberFormat.Format="0.00"; }
        return row-first;
    }
    public RecordArrivalResult RecordArrival(string masterPath,string ktPath,
        IReadOnlyList<Package> selected,DateTime arrival,bool useKt=true)
    {
        lock(gate)
        {
            if(selected.Count==0)throw new InvalidOperationException("Không có kiện Hưng Yên để nhập kho.");
            var packages=Clone(selected.ToList());
            var result=new RecordArrivalResult();
            if(useKt)
            {
                if(File.Exists(ktPath))
                {
                    var kt=ReadKt(ktPath);
                    var claimed=new HashSet<string>();
                    foreach(var package in packages)
                    {
                        // Match KT by identity AND material overlap, but preserve original
                        // source package ID / departure day / container.
                        var match=kt.Where(x=>x.Identity==package.Identity &&
                            !claimed.Contains(x.Id))
                            .OrderByDescending(x=>Overlap(x,package))
                            .ThenByDescending(x=>x.PackageNo==package.PackageNo)
                            .FirstOrDefault();
                        if(match!=null){package.Items=Clone(match.Items);claimed.Add(match.Id);}
                        else result.Warnings.Add(package.Contract+" · "+package.Project+
                            ": không có kiện khớp trong KT; giữ vật tư cont đi.");
                    }
                }
                else result.Warnings.Add("Chưa có KT tổng hợp.xlsx; sử dụng vật tư từ cont đi Hưng Yên.");
            }
            var imported=ReadMaster(masterPath).DetailPackages
                .Where(x=>x.OriginPackageId.Length>0)
                .Select(x=>x.OriginPackageId).ToHashSet(StringComparer.Ordinal);
            var toWrite=new List<Package>();
            foreach(var package in packages)
            {
                if(package.Id.Length==0)
                    throw new InvalidOperationException("Kiện Hưng Yên chưa có mã nguồn; làm mới Excel.");
                if(!DateTime.TryParseExact(package.SourceDate,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                    throw new InvalidOperationException("Kiện HĐ "+package.Contract+
                        " chưa có ngày cont đi. Không gộp hoặc ghi thiếu ngày.");
                if(imported.Contains(package.Id))
                {
                    result.Warnings.Add("Đã nhập trước đây: "+package.Contract+
                        " · "+package.Project+" · cont đi "+Rules.Day(package.SourceDate)+
                        "; giữ nguyên kiện cũ.");
                    continue;
                }
                package.OriginPackageId=package.Id;
                toWrite.Add(package);
            }
            if(toWrite.Count==0)return result;
            // The same contract in two different departure days or separate blocks is
            // appended as distinct packages; never remove other rows by identity/day.
            var written=RecordSelectedArrival(masterPath,toWrite,arrival,requireContainer:false);
            result.Contracts=written.Contracts;
            result.Packages=written.Packages;
            result.Lines=written.Lines;
            result.Warnings.AddRange(written.Warnings);
            return result;
        }
    }
    // Incremental, verified AI arrival import. Never replace all packages of an identity/day.
    // A package remains distinct by original source ID even if contract, project and quantity are equal.
    public RecordArrivalResult RecordSelectedArrival(string masterPath,
        IReadOnlyList<Package> selected, DateTime arrival, bool requireContainer=true)
    {
        lock (gate)
        {
            if (selected.Count==0) throw new InvalidOperationException("Chưa chọn kiện về thực tế.");
            var day=arrival.ToString("yyyy-MM-dd");
            var current=ReadMaster(masterPath);
            var already=current.DetailPackages.Where(x=>x.OriginPackageId.Length>0)
                .Select(x=>x.OriginPackageId).ToHashSet(StringComparer.Ordinal);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var incoming in selected)
            {
                if (incoming.Id.Length==0 || !ids.Add(incoming.Id))
                    throw new InvalidOperationException("Thiếu hoặc trùng ID kiện nguồn.");
                if (incoming.Items==null || !incoming.Items.Any(x=>x.IsHungYen))
                    throw new InvalidOperationException("Kiện HĐ " + incoming.Contract +
                        " không có dòng vật tư Hưng Yên để ghi. Chưa nhập kho.");
                if (requireContainer && incoming.ContainerNumber.Length==0)
                    throw new InvalidOperationException("Thiếu số container cho kiện " + incoming.Contract + ".");
                if (!DateTime.TryParseExact(incoming.SourceDate,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
                    throw new InvalidOperationException("Thiếu hoặc sai ngày cont đi cho kiện " +
                        incoming.Contract + ". Phải đối chiếu ngày cont đi trước khi ghi Tổng hợp.xlsx.");
                if (already.Contains(incoming.Id))
                {
                    var history=current.DetailPackages
                        .Where(x=>x.OriginPackageId==incoming.Id)
                        .Select(x=>x.ArrivalDate).Distinct().OrderBy(x=>x)
                        .Select(x=>x.Length==0?"chưa rõ (xem cột B)":Rules.Day(x));
                    throw new InvalidOperationException("KHÔNG NHẬP TRÙNG: HĐ "+incoming.Contract+
                        " · "+incoming.Project+" · kiện "+incoming.PackageNo+
                        " đã nhập ngày "+string.Join(", ",history)+
                        " trong Tổng hợp.xlsx / chi tiết. Không ghi thêm; bấm Tìm kiện để cập nhật.");
                }
                // For legacy records without provenance, exact same goods on the same day
                // could already exist: stop and request review rather than overwrite or silently duplicate.
                var suspicious=current.DetailPackages.Any(old=>
                    old.OriginPackageId.Length==0 && old.Identity==incoming.Identity &&
                    old.ArrivalDate==day && ItemCounts(old.Items).OrderBy(x=>x.Key)
                        .SequenceEqual(ItemCounts(incoming.Items).OrderBy(x=>x.Key)));
                if (suspicious) throw new InvalidOperationException(
                    "Tổng hợp.xlsx đã có kiện cùng hợp đồng, công trình, số lượng trong ngày " +
                    Rules.Day(day) + " nhưng chưa lưu mã cont/kiện gốc. Cần đối chiếu thủ công trước khi nhập.");
            }
            using var wb=new XLWorkbook(masterPath);
            var ws=wb.Worksheets.FirstOrDefault(x=>Rules.Code(x.Name)=="CHITIET")??wb.AddWorksheet("chi tiết");
            ws.Cell(2,2).Value="Ngày cont về";
            var result=new RecordArrivalResult{Packages=selected.Count,
                Contracts=selected.Select(x=>Rules.Contract(x.Contract)).Distinct().Count()};
            foreach(var package in selected)
                result.Lines+=AppendDetailPackage(ws,package,day);
            AtomicSave(wb,masterPath);
            return result;
        }
    }

    public int DeleteArrivalPackage(string masterPath,Package selected)
    {
        lock(gate)
        {
            if(!File.Exists(masterPath))throw new FileNotFoundException("Không tìm thấy Tổng hợp.xlsx.",masterPath);
            if(string.IsNullOrWhiteSpace(selected.ArrivalDate))throw new InvalidOperationException("Chỉ có thể xóa kiện đã có ngày cont về.");
            var current=ReadMaster(masterPath).DetailPackages.FirstOrDefault(x=>x.Id==selected.Id);
            if(current==null)throw new InvalidOperationException("Kiện đã thay đổi hoặc bị xóa trong file. Hãy tải lại danh sách rồi thử lại.");
            if(current.SourceRowStart<3||current.SourceRowEnd<current.SourceRowStart)throw new InvalidOperationException("Không xác định được các dòng của kiện trong trang chi tiết.");
            using var wb=new XLWorkbook(masterPath);
            var ws=wb.Worksheets.FirstOrDefault(x=>Rules.Code(x.Name)=="CHITIET")??throw new InvalidOperationException("Không tìm thấy trang chi tiết trong Tổng hợp.xlsx.");
            RemovePackageRows(ws,[current]);
            AtomicSave(wb,masterPath);
            return current.Items.Count;
        }
    }
    public int DeleteContPackage(string masterPath,Package selected)
    {
        lock(gate)
        {
            if(!File.Exists(masterPath))throw new FileNotFoundException("Không tìm thấy Tổng hợp.xlsx.",masterPath);
            var current=ReadMaster(masterPath).ContPackages.FirstOrDefault(x=>x.Id==selected.Id);
            if(current==null)throw new InvalidOperationException("Kiện đã thay đổi hoặc bị xóa trong file. Hãy tải lại danh sách.");
            using var wb=new XLWorkbook(masterPath);
            var ws=wb.Worksheets.FirstOrDefault(x=>Rules.Code(x.Name)==Rules.Code(ContSheet))??throw new InvalidOperationException("Không tìm thấy trang Cont đi Hưng Yên.");
            var rows=new List<int>();
            for(var row=current.SourceRowStart;row<=current.SourceRowEnd;row++)
            {
                var contract=Rules.CleanContract(ws.Cell(row,4).GetString());
                if(Rules.Identity(contract,ws.Cell(row,2).GetString(),ws.Cell(row,3).GetString())==current.Identity)rows.Add(row);
            }
            if(rows.Count==0)throw new InvalidOperationException("Không xác định được dòng của kiện đã chọn.");
            var sorted=rows.Distinct().Order().ToArray();
            foreach(var merge in ws.MergedRanges.Where(x=>sorted.Any(r=>r>=x.RangeAddress.FirstAddress.RowNumber&&r<=x.RangeAddress.LastAddress.RowNumber)).ToList())merge.Unmerge();
            foreach(var r in sorted.Reverse())ws.Row(r).Delete();
            AtomicSave(wb,masterPath);return rows.Count;
        }
    }
    public int ApplyKt(string masterPath,string ktPath)
    {
        lock(gate)
        {
            if(!File.Exists(ktPath)) throw new FileNotFoundException("Không tìm thấy KT tổng hợp.xlsx.",ktPath);
            var incoming=ReadKt(ktPath);
            if(incoming.Count==0) throw new InvalidOperationException("Không có kiện Hưng Yên hợp lệ trong KT. Giữ nguyên Tổng hợp.xlsx.");
            var current=ReadMaster(masterPath);
            // KT is a fallback source: preserve the master as the primary record
            // and import only arrival packages that are absent from it.
            static string PackageKey(Package x)=>$"{x.Identity}|{x.ArrivalDate}|{x.PackageNo}";
            var existing=current.DetailPackages.Select(PackageKey).ToHashSet(StringComparer.Ordinal);
            var missing=incoming.Where(x=>!existing.Contains(PackageKey(x))).ToList();
            if(missing.Count==0)return 0;
            using var wb=new XLWorkbook(masterPath);var ws=wb.Worksheets.FirstOrDefault(x=>Rules.Code(x.Name)=="CHITIET")??wb.AddWorksheet("chi tiết");
            ws.Cell(2,2).Value="Ngày cont về";
            int lines=0;foreach(var package in missing) lines+=AppendDetailPackage(ws,package,package.ArrivalDate.Length>0?package.ArrivalDate:package.SourceDate);
            AtomicSave(wb,masterPath);return lines;
        }
    }

    private static string Workspace(AppSettings settings) => Path.GetDirectoryName(Path.GetFullPath(settings.MasterPath.Length>0?settings.MasterPath:Path.Combine(AppContext.BaseDirectory,"Tổng hợp.xlsx")))!;
    public List<ShipmentRecord> ReadShipments(AppSettings settings)
    {
        lock(gate)
        {
            var files=new List<string>();
            var standard=settings.ProductExportPath.Length>0?settings.ProductExportPath:Path.Combine(Workspace(settings),"Thành phẩm xuất kho.xlsx");
            if(File.Exists(standard)) files.Add(standard);
            var exportFolder=Path.Combine(Workspace(settings),"XUẤT HÀNG");
            if(Directory.Exists(exportFolder)) files.AddRange(Directory.EnumerateFiles(exportFolder,"*.xlsx").Where(x=>Regex.IsMatch(Path.GetFileNameWithoutExtension(x),@"^\d{2}-\d{2}-\d{4}$")));
            var result=new List<ShipmentRecord>();
            foreach(var path in files)
            {
                var stamp=Stamp(path);
                if(shipmentCache.TryGetValue(path,out var cached)&&cached.Stamp==stamp) { result.AddRange(Clone(cached.Rows));continue; }
                var rows=new List<ShipmentRecord>();
                var sheets=ReadSheets(path,maxColumn:20,firstOrExportSheet:true);var sheet=sheets.FirstOrDefault();
                if(sheet==null) continue;
                int header=0,contractCol=0,projectCol=2,companyCol=3,dateCol=0,noteCol=6;
                foreach(var row in sheet.Rows.Keys.Take(15))
                {
                    foreach(var pair in sheet.Rows[row])
                    {
                        var text=Rules.Code(pair.Value.Text);
                        if(text.Contains("HOPDONG")||text is "SOHD" or "SHD") {contractCol=pair.Key;header=row;}
                        else if(text.Contains("CONGTRINH")) projectCol=pair.Key;
                        else if(text is "CONGTY" or "TENCONGTY" or "COMPANY") companyCol=pair.Key;
                        else if(text.Contains("NGAYXUAT")) dateCol=pair.Key;
                        else if(text.Contains("GHICHU")) noteCol=pair.Key;
                    }
                    if(header==row) break;
                }
                if(contractCol==0) continue;
                var fallback="";
                if(DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path),"dd-MM-yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)) fallback=day.ToString("yyyy-MM-dd");
                if(path==standard && dateCol==0) dateCol=5;
                foreach(var row in sheet.Rows.Keys.Where(x=>x>header))
                {
                    var contract=Rules.CleanContract(sheet.Text(row,contractCol));
                    if(contract.Length==0||!Regex.IsMatch(contract,@"\d")) continue;
                    var note=sheet.Text(row,noteCol);
                    var record=new ShipmentRecord {Contract=contract,Project=sheet.Text(row,projectCol),Company=sheet.Text(row,companyCol),Date=dateCol>0?sheet.Day(row,dateCol):fallback,Source=Path.GetFileName(path),Note=note};
                    if(record.Date.Length==0)record.Date=fallback;
                    record.SlipId=Regex.Match(note,@"HISA\s+SLIP\s+ID\s*:\s*([^;|]+)",RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    record.SlipCode=Regex.Match(note,@"Mã\s+phiếu\s*:\s*([^;|]+)",RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                    rows.Add(record);
                }
                shipmentCache[path]=(stamp,rows);result.AddRange(Clone(rows));
            }
            return result.DistinctBy(x=>$"{Rules.Identity(x.Contract,x.Project,x.Company)}|{x.Date}|{x.SlipId}|{x.Source}").ToList();
        }
    }
    public void SyncExportLedgers(AppSettings settings,IReadOnlyList<Slip> slips)
    {
        lock(gate)
        {
            var path=settings.ProductExportPath.Length>0?settings.ProductExportPath:Path.Combine(Workspace(settings),"Thành phẩm xuất kho.xlsx");
            var ids=slips.Select(x=>x.Id).ToHashSet();
            using(var wb=File.Exists(path)?new XLWorkbook(path):new XLWorkbook())
            {
                var ws=wb.Worksheets.FirstOrDefault(x=>x.Name=="Sheet1")??wb.Worksheets.FirstOrDefault()??wb.AddWorksheet("Sheet1");
                if((ws.LastRowUsed()?.RowNumber()??0)<2)
                {
                    ws.Cell(1,1).Value="THEO DÕI THÀNH PHẨM XUẤT KHO";
                    string[] headers=["STT","Tên công trình","Công ty","Hợp đồng","Ngày xuất","Ghi chú"];
                    for(int col=0;col<headers.Length;col++) ws.Cell(2,col+1).Value=headers[col];StyleHeader(ws.Range(2,1,2,6));
                }
                else if(!Enumerable.Range(1,6).Any(c=>Rules.Code(ws.Cell(2,c).GetString()).Contains("HOPDONG"))) throw new InvalidDataException("Không nhận diện được mẫu Thành phẩm xuất kho.xlsx. Giữ nguyên file.");
                var last=ws.LastRowUsed()?.RowNumber()??2;
                for(int row=last;row>=3;row--)
                {
                    var match=Regex.Match(ws.Cell(row,6).GetString(),@"HISA\s+SLIP\s+ID\s*:\s*([^;|]+)",RegexOptions.IgnoreCase);
                    if(match.Success&&ids.Contains(match.Groups[1].Value.Trim())) ws.Row(row).Delete();
                }
                int next=Math.Max(3,(ws.LastRowUsed()?.RowNumber()??2)+1);
                foreach(var slip in slips.Where(x=>x.Status=="delivered"))
                {
                    var records=ExportRecords(slip);
                    foreach(var record in records)
                    {
                        if(next>3)
                        {
                            ws.Row(next).Style=ws.Row(3).Style;
                            // Keep calculated template fields beyond the six tracking
                            // columns, with relative references shifted to the new row.
                            for(int col=7;col<=(ws.LastColumnUsed()?.ColumnNumber()??6);col++)
                                if(ws.Cell(3,col).HasFormula)ws.Cell(next,col).FormulaR1C1=ws.Cell(3,col).FormulaR1C1;
                        }
                        ws.Cell(next,1).FormulaA1=$"ROW()-2";ws.Cell(next,2).Value=record.Project;ws.Cell(next,3).Value=record.Company;SetContract(ws.Cell(next,4),record.Contract);SetDate(ws.Cell(next,5),slip.HandoverDate);
                        var serials=string.Join("; ",MotorSerials.ActiveRows(slip).Where(x=>Rules.Identity(x.Contract,x.Project,x.Company)==Rules.Identity(record.Contract,record.Project,record.Company)).Select(x=>x.SerialNumber));
                        if(serials.Length>20000)serials=serials[..20000]+"… (đầy đủ trong bảng Serial máy kéo của Sổ theo dõi xuất hàng.xlsx)";
                        ws.Cell(next,6).Value=$"HISA Slip ID: {slip.Id} | Mã phiếu: {slip.SlipCode}"+(slip.Note.Length>0?" | "+slip.Note:"")+(serials.Length>0?" | Serial máy kéo: "+serials:"");next++;
                    }
                }
                ws.Column(2).Width=Math.Max(45,ws.Column(2).Width);ws.Column(3).Width=Math.Max(22,ws.Column(3).Width);ws.Column(4).Width=17;ws.Column(5).Width=16;ws.Column(6).Width=60;
                AtomicSave(wb,path);
            }
            var folder=Path.Combine(Workspace(settings),"XUẤT HÀNG");Directory.CreateDirectory(folder);
            using var ledger=new XLWorkbook();
            var orders=ledger.AddWorksheet("Đơn đã xuất");var material=ledger.AddWorksheet("Vật tư bàn giao");var packages=ledger.AddWorksheet("Kiện Hưng Yên");var plans=ledger.AddWorksheet("Kế hoạch và trạng thái");
            WriteHeader(orders,["Mã phiếu","Ngày xuất","Số hợp đồng","Tên công trình","Tên công ty","Số kiện Hưng Yên","Số dòng bàn giao","Tên phiếu","Thời điểm ghi nhận","Ghi chú"]);
            WriteHeader(material,["Mã phiếu","Ngày xuất","Số hợp đồng","Tên công trình","Tên công ty","Nhóm vật tư","Tên vật tư","ĐVT","Số lượng","Ngày cont","Tệp kiện","Ghi chú","Serial máy kéo"]);
            WriteHeader(packages,["Mã phiếu","Ngày xuất","Số hợp đồng","Tên công trình","Tên công ty","Ngày cont","Tệp kiện","Mã kiện","Tên vật tư","ĐVT","Số lượng","Nhóm vật tư","Ghi chú"]);
            WriteHeader(plans,["Mã phiếu","Ngày kế hoạch / xuất","Hợp đồng","Trạng thái","Lý do hoãn / hủy","Cập nhật"]);
            int ro=2,rm=2,rp=2,rs=2;
            foreach(var slip in slips)
            {
                WriteValues(plans,rs++,[slip.SlipCode,slip.HandoverDate,string.Join(", ",slip.Contracts),slip.StatusLabel,slip.Status=="cancelled"?slip.CancelReason:slip.DelayReason,slip.UpdatedAt]);
                if(slip.Status!="delivered")continue;
                var exported=ExportRecords(slip);
                var exportedIds=exported.Select(x=>Rules.Identity(x.Contract,x.Project,x.Company)).ToHashSet();
                foreach(var record in exported)
                {
                    var identity=Rules.Identity(record.Contract,record.Project,record.Company);
                    var contractOnly=slip.ContractOnly.Any(x=>Rules.Identity(x.Contract,x.Project,x.Company)==identity);
                    WriteValues(orders,ro++,[slip.SlipCode,slip.HandoverDate,record.Contract,record.Project,record.Company,contractOnly?0:slip.ContractPackages.Count(x=>x.Identity==identity),slip.Lines.Count(x=>x.Include&&Rules.Identity(x.Contract,x.Project,x.Company)==identity),slip.Name,slip.UpdatedAt,slip.Note]);
                }
                foreach(var line in slip.Lines.Where(x=>x.Include)) WriteValues(material,rm++,[slip.SlipCode,slip.HandoverDate,line.Contract,line.Project,line.Company,line.Group,line.Material,line.Unit,line.Quantity,line.SourceDate,line.SourceFile,line.Note,ExcelSerialCell(slip,line)]);
                foreach(var p in slip.ContractPackages.Where(x=>exportedIds.Contains(x.Identity)&&!slip.ContractOnly.Any(c=>Rules.Identity(c.Contract,c.Project,c.Company)==x.Identity))) foreach(var line in p.Items) WriteValues(packages,rp++,[slip.SlipCode,slip.HandoverDate,p.Contract,p.Project,p.Company,p.SourceDate,p.SourceFile,p.Id,line.Material,line.Unit,line.Quantity,line.Group,line.Note]);
            }
            WriteMotorSerialSheet(ledger,slips);
            foreach(var ws in ledger.Worksheets) FormatExport(ws);
            AtomicSave(ledger,Path.Combine(folder,"Sổ theo dõi xuất hàng.xlsx"));
        }
    }
    private static List<(string Contract,string Project,string Company)> ExportRecords(Slip slip)
    {
        var lines=slip.Lines??[];
        // Snapshots retain the complete originally selected contracts, even when
        // every material for one of them has later been unchecked. They are not
        // evidence that the unchecked contract was physically exported.
        var source=(lines.Count>0?lines.Where(x=>x.Include).Select(x=>(x.Contract,x.Project,x.Company)):Enumerable.Empty<(string Contract,string Project,string Company)>())
            .Concat((slip.ContractPackages??[]).Where(p=>lines.Count==0).Select(x=>(x.Contract,x.Project,x.Company)))
            .Concat((slip.ContractOnly??[]).Select(x=>(x.Contract,x.Project,x.Company)));
        return source.Where(x=>x.Contract.Length>0).DistinctBy(x=>Rules.Identity(x.Contract,x.Project,x.Company)).ToList();
    }
    private static void WriteHeader(IXLWorksheet ws,string[] headers)
    {
        for(int col=0;col<headers.Length;col++)ws.Cell(1,col+1).Value=headers[col];StyleHeader(ws.Range(1,1,1,headers.Length));
    }
    private static void WriteMotorSerialSheet(XLWorkbook workbook,IEnumerable<Slip> slips)
    {
        var sheet=workbook.AddWorksheet("Serial máy kéo");
        WriteHeader(sheet,["Mã phiếu","Ngày kế hoạch / xuất","Trạng thái","Số hợp đồng","Công trình","Công ty","Máy kéo / model","Máy thứ","Serial"]);
        var row=2;
        foreach(var slip in slips)foreach(var serial in MotorSerials.ActiveRows(slip))
            WriteValues(sheet,row++,[slip.SlipCode,slip.HandoverDate,slip.StatusLabel,serial.Contract,serial.Project,serial.Company,serial.Material,serial.UnitIndex,serial.SerialNumber]);
    }
    private static string ExcelSerialCell(Slip slip,MaterialLine line)
    {
        var value=MotorSerials.SerialText(slip,line);
        return value.Length<=30000?value:value[..30000]+"… (xem đủ từng máy trong bảng Serial máy kéo)";
    }
    private static void WriteValues(IXLWorksheet ws,int row,object?[] values)
    {
        for(int col=0;col<values.Length;col++)
        {
            var cell=ws.Cell(row,col+1);var value=values[col];
            if(value==null)continue;
            if(value is decimal d)cell.Value=(double)d;else if(value is int n)cell.Value=n;else if(value is double x)cell.Value=x;else if(value is DateTime day) {cell.Value=day;cell.Style.NumberFormat.Format="dd/MM/yyyy";}else cell.Value=value.ToString()??"";
        }
    }
    private static void FormatExport(IXLWorksheet ws)
    {
        ws.SheetView.FreezeRows(1);ws.ShowGridLines=false;ws.Row(1).Height=32;
        var last=ws.LastCellUsed();if(last!=null) {ws.Range(1,1,last.Address.RowNumber,last.Address.ColumnNumber).SetAutoFilter();ws.RangeUsed()!.Style.Font.FontName="Calibri";ws.RangeUsed()!.Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;}
        foreach(var col in ws.ColumnsUsed()) col.Width=col.ColumnNumber() is 4 or 5 or 7?42:22;
        ws.PageSetup.PageOrientation=XLPageOrientation.Landscape;ws.PageSetup.PaperSize=XLPaperSize.A4Paper;ws.PageSetup.FitToPages(1,0);ws.PageSetup.SetRowsToRepeatAtTop(1,1);
    }
    public void ExportReport(string path,ContractReport report)
    {
        using var wb=new XLWorkbook();var summary=wb.AddWorksheet("Tổng hợp");
        WriteHeader(summary,["Chỉ tiêu","Số lượng"]);
        object?[][] metrics=[["Số hợp đồng yêu cầu",report.RequestedCount],["Hợp đồng có kiện Hưng Yên",report.ContractCount],["Công trình",report.ProjectCount],["Kiện Hưng Yên",report.PackageCount],["Dòng vật tư",report.LineCount],["Hợp đồng chưa có dữ liệu",report.Missing.Count],["Hợp đồng đã lên phiếu",report.WithSlips],["Hợp đồng có phiếu hủy",report.CancelledCount]];
        int row=2;foreach(var metric in metrics)WriteValues(summary,row++,metric);
        foreach(var category in report.Categories)WriteValues(summary,row++,[category.Category,category.ContractCount,category.ProjectCount,category.LineCount,category.QuantityLabel]);
        var details=wb.AddWorksheet("Chi tiết vật tư");WriteHeader(details,["Số hợp đồng","Công trình","Công ty","Tên vật tư","ĐVT","Số lượng","Nhóm vật tư","Ngày cont về","Kiện","Trạng thái","Ghi chú"]);
        row=2;foreach(var contract in report.Rows)foreach(var line in contract.Materials)WriteValues(details,row++,[contract.Contract,contract.Project,contract.Company,line.Material,line.Unit,line.Quantity,line.Group,line.ArrivalDate,line.PackageNo,contract.StatusLabel,line.Note]);
        var warnings=wb.AddWorksheet("Cảnh báo");WriteHeader(warnings,["Số hợp đồng","Công trình","Công ty","Nội dung","Trạng thái"]);row=2;
        foreach(var contract in report.Missing.Concat(report.NoMaterialMatch).Concat(report.DateMismatch))WriteValues(warnings,row++,[contract.Contract,contract.Project,contract.Company,contract.Warning,contract.StatusLabel]);
        foreach(var warning in report.Warnings)WriteValues(warnings,row++,["","","",warning,""]);
        var status=wb.AddWorksheet("Lịch sử và trạng thái");WriteHeader(status,["Trạng thái","Số hợp đồng","Công trình","Công ty","Mã phiếu","Ngày xuất / kế hoạch","Lý do"]);row=2;
        foreach(var group in report.StatusDetails)foreach(var contract in group.Value)
        {
            if(contract.Slips.Count==0)WriteValues(status,row++,[Rules.StatusLabel(group.Key),contract.Contract,contract.Project,contract.Company,"","",contract.Warning]);
            foreach(var slip in contract.Slips)WriteValues(status,row++,[Rules.StatusLabel(group.Key),contract.Contract,contract.Project,contract.Company,slip.SlipCode,slip.HandoverDate,slip.Status=="cancelled"?slip.CancelReason:slip.DelayReason]);
        }
        foreach(var ws in wb.Worksheets)FormatExport(ws);AtomicSave(wb,path);
    }
    public void ExportDaily(string path,DailyReport report)
    {
        using var wb=new XLWorkbook();var summary=wb.AddWorksheet("Tổng hợp ngày");WriteHeader(summary,["Ngày","Trạng thái","Số phiếu"]);int row=2;
        foreach(var group in report.StatusCounts)WriteValues(summary,row++,[report.Date,Rules.StatusLabel(group.Key),group.Value]);
        var slips=wb.AddWorksheet("Danh sách phiếu");WriteHeader(slips,["Mã phiếu","Ngày kế hoạch / xuất","Trạng thái","Số hợp đồng","Dòng vật tư","Ghi chú / lý do"]);row=2;
        foreach(var slip in report.Slips)WriteValues(slips,row++,[slip.SlipCode,slip.HandoverDate,slip.StatusLabel,slip.ContractLabel,slip.LineCount,slip.Status=="cancelled"?slip.CancelReason:slip.Status=="delayed"?slip.DelayReason:slip.Note]);
        var details=wb.AddWorksheet("Vật tư từng phiếu");WriteHeader(details,["Mã phiếu","Trạng thái","Hợp đồng","Công trình","Công ty","Tên vật tư","Nhóm","ĐVT","Số lượng","Ghi chú","Serial máy kéo"]);row=2;
        foreach(var slip in report.Slips)foreach(var line in slip.Lines.Where(x=>x.Include))WriteValues(details,row++,[slip.SlipCode,slip.StatusLabel,line.Contract,line.Project,line.Company,line.Material,line.Group,line.Unit,line.Quantity,line.Note,ExcelSerialCell(slip,line)]);
        WriteMotorSerialSheet(wb,report.Slips);
        var delayed=wb.AddWorksheet("Hoãn còn chờ");WriteHeader(delayed,["Mã phiếu","Ngày kế hoạch","Hợp đồng","Lý do hoãn"]);row=2;
        foreach(var slip in report.OutstandingDelayed)WriteValues(delayed,row++,[slip.SlipCode,slip.HandoverDate,slip.ContractLabel,slip.DelayReason]);
        var history=wb.AddWorksheet("Xuất theo sổ");WriteHeader(history,["Ngày xuất","Hợp đồng","Công trình","Công ty","Nguồn","Mã phiếu","Ghi chú"]);row=2;
        foreach(var shipment in report.LegacyDeliveries)WriteValues(history,row++,[shipment.Date,shipment.Contract,shipment.Project,shipment.Company,shipment.Source,shipment.SlipCode,shipment.Note]);
        foreach(var ws in wb.Worksheets)FormatExport(ws);AtomicSave(wb,path);
    }
}
