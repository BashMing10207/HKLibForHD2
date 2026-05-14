Add-Type -AssemblyName System.Xml.Linq
$xml = "<root>test</root><!-- PREPEND_DATA: 123 -->"
$element = [System.Xml.Linq.XElement]::Parse($xml)
Write-Output $element.Name.LocalName
