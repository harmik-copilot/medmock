using Med.Domain;

namespace Med.Ingest;

/// <summary>
/// The seam between the clinic's PACS/VNA and the sharing platform. In real life this would speak DICOM
/// (C-FIND / C-MOVE, or DICOMweb) across a secure connector. Here it is just an interface,
/// so tests can feed in good and bad studies on demand.
/// </summary>
public interface IPacsSource
{
    IReadOnlyList<IncomingStudy> Fetch(string providerId);
}
