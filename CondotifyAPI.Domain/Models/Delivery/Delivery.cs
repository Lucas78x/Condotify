namespace CondotifyAPI.Domain.Models.Delivery
{
    public class Delivery
    {
        private Delivery() { }


        public Guid Id { get; private set; }

        public DeliveryTypeEnum Type { get; private set; }
        public DeliveryStatusEnum Status { get; private set; }

        public string Name { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public string TrackingCode { get; private set; } = string.Empty;

        public string PhotoUrl { get; private set; } = string.Empty;
        public string DeliveryProofUrl { get; private set; } = string.Empty;

        public Guid? ReceivedId { get; private set; }
        public string ReceivedBy { get; private set; } = string.Empty;
        public DateTime? ReceivedAt { get; private set; }

        public Guid? DeliveredToId { get; private set; }
        public string DeliveredTo { get; private set; } = string.Empty;
        public DateTime? DeliveredAt { get; private set; }

        public DateTime CreatedAt { get; private set; }
        public DateTime UpdatedAt { get; private set; }

        public Guid LicenseId { get; private set; }


        public static Delivery Create(
            DeliveryTypeEnum type,
            string name,
            Guid licensedId,
            string? description = null,
            string? trackingCode = null,
            string? photoUrl = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Nome da entrega é obrigatório");

            return new Delivery
            {
                Id = Guid.NewGuid(),
                Type = type,
                Status = DeliveryStatusEnum.Pending,
                Name = name,
                Description = description ?? string.Empty,
                TrackingCode = trackingCode ?? string.Empty,
                PhotoUrl = photoUrl ?? string.Empty,
                LicenseId = licensedId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Marca a encomenda como recebida na portaria
        /// </summary>
        public void Receive(Guid receivedId, string receivedBy)
        {
            if (Status != DeliveryStatusEnum.Pending)
                throw new InvalidOperationException("Entrega não está pendente");

            ReceivedId = receivedId;
            ReceivedBy = receivedBy;
            ReceivedAt = DateTime.UtcNow;
            Status = DeliveryStatusEnum.Received;

            Touch();
        }

        /// <summary>
        /// Registra a retirada da encomenda
        /// </summary>
        public void Deliver(
            string deliveredTo,
            Guid? deliveredToId = null,
            string? deliveryProofUrl = null)
        {
            if (Status != DeliveryStatusEnum.Received)
                throw new InvalidOperationException("Entrega ainda não foi recebida");

            DeliveredTo = deliveredTo;
            DeliveredToId = deliveredToId;
            DeliveryProofUrl = deliveryProofUrl ?? string.Empty;
            DeliveredAt = DateTime.UtcNow;
            Status = DeliveryStatusEnum.Delivered;

            Touch();
        }

        /// <summary>
        /// Atualiza a foto da encomenda
        /// </summary>
        public void UpdatePhoto(string photoUrl)
        {
            PhotoUrl = photoUrl;
            Touch();
        }

        /// <summary>
        /// Atualiza o status manualmente (uso administrativo)
        /// </summary>
        public void ChangeStatus(DeliveryStatusEnum status)
        {
            Status = status;
            Touch();
        }

        /// <summary>
        /// Atualiza o UpdatedAt
        /// </summary>
        private void Touch()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
