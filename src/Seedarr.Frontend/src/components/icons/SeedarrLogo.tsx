interface LogoProps {
  size?: number;
  className?: string;
}

function SeedarrLogo({ size = 28, className }: LogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 512 512"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
    >
      <circle cx="256" cy="256" r="256" fill="#3A3F51" />
      <path
        d="M256 120 C310 170 340 230 340 290 C340 336 302 372 256 372 C210 372 172 336 172 290 C172 230 202 170 256 120Z"
        fill="#35C5F4"
      />
      <path
        d="M256 160 L256 340"
        stroke="#3A3F51"
        strokeWidth="8"
        strokeLinecap="round"
      />
      <path
        d="M256 220 C230 240 215 270 220 300"
        stroke="#3A3F51"
        strokeWidth="7"
        strokeLinecap="round"
        fill="none"
      />
      <path
        d="M256 220 C282 240 297 270 292 300"
        stroke="#3A3F51"
        strokeWidth="7"
        strokeLinecap="round"
        fill="none"
      />
      <path
        d="M256 120 L256 60"
        stroke="#35C5F4"
        strokeWidth="8"
        strokeLinecap="round"
      />
      <path
        d="M256 80 C236 65 220 70 215 85 C210 100 225 105 256 95"
        fill="#35C5F4"
      />
      <path
        d="M256 65 C276 50 292 55 297 70 C302 85 287 90 256 80"
        fill="#35C5F4"
      />
    </svg>
  );
}

export default SeedarrLogo;
