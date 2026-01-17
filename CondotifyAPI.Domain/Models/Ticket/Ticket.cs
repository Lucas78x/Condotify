using CondotifyAPI.Domain.Models.Audit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CondotifyAPI.Domain.Models.Ticket
{
    public class Ticket
    {
        public Guid Id { get; private set; }

        public Guid UnitId { get; private set; }
        public string Title { get; private set; }

        public string Barcode { get; private set; }
        public string BarcodeUrl { get; private set; }

        public DateTime CreatedDate { get; private set; }
        public DateTime ExpiredDate { get; private set; }

        public TicketStatusTypeEnum Status { get; private set; }

        public bool IsSecondCopy { get; private set; }
        public Guid? OriginalTicketId { get; private set; }

        public Guid LicenseId { get; private set; }

        public List<TicketAudit> Audits { get; private set; }

        protected Ticket() { }

        private Ticket(
            Guid unitId,
            string title,
            DateTime expiredDate,
            Guid licenseId,
            bool isSecondCopy,
            Guid? originalTicketId)
        {
            Id = Guid.NewGuid();
            UnitId = unitId;
            Title = title;
            ExpiredDate = expiredDate;
            LicenseId = licenseId;
            IsSecondCopy = isSecondCopy;
            OriginalTicketId = originalTicketId;

            Barcode = GenerateBarcode();
            BarcodeUrl = GenerateBarcodeUrl(Barcode);

            Status = TicketStatusTypeEnum.Send;
            CreatedDate = DateTime.UtcNow;

            AddAudit("Ticket criado", Status);
        }

        // 🔹 Factory
        public static Ticket Create(
            Guid unitId,
            string title,
            DateTime expiredDate,
            Guid licenseId,
            bool isSecondCopy,
            Guid? originalTicketId,
            DateTime createdDate,
            DateTime updatedDate)
        {
            //if (expiredDate <= DateTime.UtcNow)
            //    throw new DomainException("A data de expiração deve ser futura.");

            //if (isSecondCopy && originalTicketId == null)
            //    throw new DomainException("Segunda via exige ticket original.");

            return new Ticket(
                unitId,
                title,
                expiredDate,
                licenseId,
                isSecondCopy,
                originalTicketId);
        }

        // 🔹 Regras de negócio
        public void Cancel(string reason)
        {
            //if (Status == TicketStatusTypeEnum.Canceled)
            //    return;

            //Status = TicketStatusTypeEnum.Canceled;
            //AddAudit($"Ticket cancelado: {reason}", Status);
        }

        public void Expire()
        {
            if (Status == TicketStatusTypeEnum.Expired)
                return;

            Status = TicketStatusTypeEnum.Expired;
            AddAudit("Ticket expirado", Status);
        }

        // 🔹 Auditoria
        private void AddAudit(string action, TicketStatusTypeEnum status)
        {
            //_audit.Add(new TicketAudit(
            //    Id,
            //    action,
            //    status,
            //    DateTime.UtcNow));
        }

        // 🔹 Helpers
        private static string GenerateBarcode()
            => Guid.NewGuid().ToString("N")[..12].ToUpper();

        private static string GenerateBarcodeUrl(string barcode)
            => $"https://api.condotify.com/barcodes/{barcode}";
    }
}
