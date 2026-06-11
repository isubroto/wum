// src/WUM.Core/Models/UpdateHistory.cs
using System;

namespace WUM.Core.Models
{
    public class UpdateHistory
    {
        public string   Id            { get; set; } = Guid.NewGuid().ToString();
        public string   UpdateId      { get; set; } = string.Empty;
        public string   Title         { get; set; } = string.Empty;
        public string   KBArticle     { get; set; } = string.Empty;
        public DateTime InstalledDate { get; set; }
        public bool     Success       { get; set; }
        public int      ResultCode    { get; set; }
        public string?  ErrorMessage  { get; set; }
        public string   InstalledBy   { get; set; } = string.Empty;
        public string   Operation     { get; set; } = string.Empty;
    }
}