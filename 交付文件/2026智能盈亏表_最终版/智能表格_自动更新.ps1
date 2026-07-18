$ErrorActionPreference = "Stop"

$workbookPath = Join-Path $PSScriptRoot "2026智能盈亏表.xlsx"
$temporaryPath = Join-Path $PSScriptRoot "2026智能盈亏表.updating.xlsx"
$backupPath = Join-Path $PSScriptRoot "2026智能盈亏表.更新前备份.xlsx"
$date = Get-Date -Format "yyyy-M-d"
$apiUrl = "https://api.00853lhc.com/api/HistoryOpenInfo?issueNum=$date&lotteryId=2032"

function Escape-Xml([object]$value) {
    return [System.Security.SecurityElement]::Escape([string]$value)
}

function Convert-Zodiac([string]$value) {
    switch ($value.Trim()) {
        "龍" { "龙"; break }
        "馬" { "马"; break }
        "雞" { "鸡"; break }
        "豬" { "猪"; break }
        default { $value.Trim() }
    }
}

function Get-SpecialZodiac([int]$number, [string]$yearPet) {
    $order = @("鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪")
    $pet = Convert-Zodiac $yearPet
    $index = [Array]::IndexOf($order, $pet)
    if ($index -lt 0 -or $number -lt 1 -or $number -gt 49) { return "" }
    if ($number -eq 49) { return $pet }

    $reverse = @()
    for ($i = $index; $i -ge 0; $i--) { $reverse += $order[$i] }
    for ($i = $order.Count - 1; $i -gt $index; $i--) { $reverse += $order[$i] }
    return $reverse[($number - 1) % 12]
}

function Get-WaveColor([int]$number) {
    $red = @(1, 2, 7, 8, 12, 13, 18, 19, 23, 24, 29, 30, 34, 35, 40, 45, 46)
    $blue = @(3, 4, 9, 10, 14, 15, 20, 25, 26, 31, 36, 37, 41, 42, 47, 48)
    if ($red -contains $number) { return "红波" }
    if ($blue -contains $number) { return "蓝波" }
    if ($number -ge 1 -and $number -le 49) { return "绿波" }
    return ""
}

function Add-InlineCell([System.Text.StringBuilder]$builder, [string]$reference, [object]$value, [int]$style = 0) {
    $escaped = Escape-Xml $value
    [void]$builder.Append("<c r=`"$reference`" t=`"inlineStr`" s=`"$style`"><is><t>$escaped</t></is></c>")
}

function Add-NumberCell([System.Text.StringBuilder]$builder, [string]$reference, [object]$value, [int]$style = 0) {
    $escaped = Escape-Xml $value
    [void]$builder.Append("<c r=`"$reference`" s=`"$style`"><v>$escaped</v></c>")
}

function Get-WorksheetEntryPath([System.IO.Compression.ZipArchive]$archive, [string]$sheetName) {
    $workbookEntry = $archive.GetEntry("xl/workbook.xml")
    $relationshipsEntry = $archive.GetEntry("xl/_rels/workbook.xml.rels")
    if ($null -eq $workbookEntry -or $null -eq $relationshipsEntry) {
        throw "工作簿结构不完整"
    }

    $stream = $workbookEntry.Open()
    try {
        $workbookXml = [xml]([System.IO.StreamReader]::new($stream)).ReadToEnd()
    } finally { $stream.Dispose() }

    $namespace = [System.Xml.XmlNamespaceManager]::new($workbookXml.NameTable)
    $namespace.AddNamespace("x", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
    $sheet = $workbookXml.SelectSingleNode("//x:sheets/x:sheet[@name='$sheetName']", $namespace)
    if ($null -eq $sheet) { throw "找不到工作表：$sheetName" }
    $relationshipId = $sheet.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")

    $stream = $relationshipsEntry.Open()
    try {
        $relationshipsXml = [xml]([System.IO.StreamReader]::new($stream)).ReadToEnd()
    } finally { $stream.Dispose() }

    $relationshipNamespace = [System.Xml.XmlNamespaceManager]::new($relationshipsXml.NameTable)
    $relationshipNamespace.AddNamespace("r", "http://schemas.openxmlformats.org/package/2006/relationships")
    $relationship = $relationshipsXml.SelectSingleNode("//r:Relationship[@Id='$relationshipId']", $relationshipNamespace)
    if ($null -eq $relationship) { throw "无法定位工作表文件：$sheetName" }

    $target = [string]$relationship.Target
    if ($target.StartsWith("/")) { return $target.TrimStart("/") }
    return "xl/$($target.TrimStart('./'))"
}

try {
    if (-not (Test-Path -LiteralPath $workbookPath)) {
        throw "找不到工作簿：$workbookPath"
    }

    Write-Host "正在连接开奖数据源……" -ForegroundColor Cyan
    $response = Invoke-RestMethod -Uri $apiUrl -TimeoutSec 30 -Headers @{ "User-Agent" = "Mozilla/5.0" }
    if ($response.code -ne 0) { throw "接口返回失败：$($response.message)" }

    $records = foreach ($item in $response.data) {
        $numbers = @($item.openCode -split "[,， ]+" | Where-Object { $_ } | ForEach-Object { ([int]$_).ToString("D2") })
        if ($numbers.Count -lt 7) { continue }
        $pet = Convert-Zodiac ([string]$item.pet)
        [pscustomobject]@{
            Issue = [int64]$item.issue
            OpenTime = [string]$item.openTime
            Numbers = $numbers
            YearPet = $pet
            SpecialZodiac = Get-SpecialZodiac ([int]$numbers[6]) $pet
            WaveColor = Get-WaveColor ([int]$numbers[6])
        }
    }
    $records = @($records | Sort-Object Issue -Descending)
    if ($records.Count -eq 0) { throw "接口没有返回有效开奖记录" }

    $headers = @("期号", "开奖时间", "第1码", "第2码", "第3码", "第4码", "第5码", "第6码", "特码", "年份生肖", "特码生肖", "特码波色")
    $columns = @("A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L")
    $lastRow = $records.Count + 1
    $xml = [System.Text.StringBuilder]::new()
    [void]$xml.Append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
    [void]$xml.Append('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">')
    [void]$xml.Append("<dimension ref=`"A1:L$lastRow`"/><sheetViews><sheetView workbookViewId=`"0`"><pane ySplit=`"1`" topLeftCell=`"A2`" activePane=`"bottomLeft`" state=`"frozen`"/></sheetView></sheetViews>")
    [void]$xml.Append('<sheetFormatPr defaultRowHeight="18"/><cols><col min="1" max="1" width="14" customWidth="1"/><col min="2" max="2" width="22" customWidth="1"/><col min="3" max="12" width="12" customWidth="1"/></cols><sheetData>')
    [void]$xml.Append('<row r="1" ht="24" customHeight="1">')
    for ($i = 0; $i -lt $headers.Count; $i++) { Add-InlineCell $xml "$($columns[$i])1" $headers[$i] 22 }
    [void]$xml.Append('</row>')

    $rowNumber = 2
    foreach ($record in $records) {
        [void]$xml.Append("<row r=`"$rowNumber`">")
        Add-NumberCell $xml "A$rowNumber" $record.Issue
        Add-InlineCell $xml "B$rowNumber" $record.OpenTime
        for ($i = 0; $i -lt 6; $i++) { Add-InlineCell $xml "$($columns[$i + 2])$rowNumber" $record.Numbers[$i] }
        Add-InlineCell $xml "I$rowNumber" $record.Numbers[6]
        Add-InlineCell $xml "J$rowNumber" $record.YearPet
        Add-InlineCell $xml "K$rowNumber" $record.SpecialZodiac
        Add-InlineCell $xml "L$rowNumber" $record.WaveColor
        [void]$xml.Append('</row>')
        $rowNumber++
    }
    [void]$xml.Append("</sheetData><autoFilter ref=`"A1:L$lastRow`"/></worksheet>")

    Copy-Item -LiteralPath $workbookPath -Destination $backupPath -Force
    Copy-Item -LiteralPath $workbookPath -Destination $temporaryPath -Force
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($temporaryPath, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        # 按工作表名称定位，工作表顺序改变后也不会更新错表。
        $worksheetEntryPath = Get-WorksheetEntryPath $archive "开奖记录"
        $entry = $archive.GetEntry($worksheetEntryPath)
        if ($null -eq $entry) { throw "无法定位开奖记录工作表" }
        $entry.Delete()
        $newEntry = $archive.CreateEntry($worksheetEntryPath, [System.IO.Compression.CompressionLevel]::Optimal)
        $stream = $newEntry.Open()
        try {
            $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false))
            try { $writer.Write($xml.ToString()) } finally { $writer.Dispose() }
        } finally { $stream.Dispose() }
    } finally { $archive.Dispose() }

    Move-Item -LiteralPath $temporaryPath -Destination $workbookPath -Force
    Write-Host "更新成功：共写入 $($records.Count) 条，最新期号 $($records[0].Issue)" -ForegroundColor Green
    Write-Host "现在可以打开 2026智能盈亏表.xlsx" -ForegroundColor Green
}
catch {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue }
    Write-Host "更新失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "请确认网络正常，并关闭已经打开的工作簿。" -ForegroundColor Yellow
    exit 1
}

if ($env:SMART_TABLE_NO_PAUSE -ne "1") {
    Write-Host "按任意键退出……"
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
