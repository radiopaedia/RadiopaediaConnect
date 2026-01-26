using System;
using System.Collections.Generic;

namespace RadiopaediaConnect.Models
{
    public class DicomStudyDto
    {
        public string StudyInstanceUid { get; set; }
        public string PatientName { get; set; }
        public string PatientId { get; set; }
        public string AccessionNumber { get; set; }
        public DateTime? StudyDate { get; set; }
        public string Modality { get; set; }
        public string StudyDescription { get; set; }
        public int InstanceCount { get; set; }

        public string PatientAge { get; set; }
        public string PatientSex { get; set; }
        public DateTime PatientBirthDate { get; set; }
        public string RemoteNodeName { get; set; }

        public List<DicomSeriesDto> Series { get; set; } = new List<DicomSeriesDto>();
    }

    public class DicomSeriesDto
    {
        public string SeriesInstanceUid { get; set; }
        public string StudyInstanceUid { get; set; }
        public string Modality { get; set; }
        public string SeriesDescription { get; set; }
        public int SeriesNumber { get; set; }
        public int InstanceCount { get; set; }
        public string RemoteNodeName { get; set; }
    }

    public class DicomSearchCriteria
    {
        public string PatientId { get; set; }
        public string PatientName { get; set; }
        public string AccessionNumber { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string RemoteNodeName { get; set; }
    }
}