using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Abstractions;

public partial interface IAppDbContext
{
    /// <summary>CRM'dan kiritilgan, SHIFRLANGAN tizim kalitlari (Instagram/Meta). Zaxiraga, MCP'ga, audit va logga
    /// qiymati HECH QACHON tushmaydi (<see cref="Services.StoredSecrets"/>).</summary>
    DbSet<StoredSecret> StoredSecrets { get; }
}
