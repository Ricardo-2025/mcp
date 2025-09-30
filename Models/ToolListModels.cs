using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenesysMigrationMCP.Models
{
    public sealed class ToolListItem
    {
        public required string Name { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public object? InputSchema { get; init; } // mantenha mínimo (type/properties)
    }

    public sealed class ToolListPage
    {
        public required List<ToolListItem> Items { get; init; }
        public required int Total { get; init; }
    }
}
