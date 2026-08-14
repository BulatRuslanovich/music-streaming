import { ReactNode } from "react";
import {
  ChevronDown as LucideChevronDown,
  ChevronLeft as LucideChevronLeft,
  ChevronRight as LucideChevronRight,
  ChevronUp as LucideChevronUp,
  Clock as LucideClock,
  Download as LucideDownload,
  Home as LucideHome,
  Image as LucideImage,
  LogOut as LucideLogOut,
  type LucideIcon,
  Pencil as LucidePencil,
  Plus as LucidePlus,
  Search as LucideSearch,
  Shield as LucideShield,
  Trash2 as LucideTrash,
  TriangleAlert as LucideTriangleAlert,
  Upload as LucideUpload,
  Volume2 as LucideVolume2,
  VolumeX as LucideVolumeX,
  X as LucideX,
} from "lucide-react";

interface IconProps {
  size?: number;
  className?: string;
}

function Svg({
  size = 20,
  className,
  children,
  filled = false,
}: IconProps & { children: ReactNode; filled?: boolean }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill={filled ? "currentColor" : "none"}
      stroke={filled ? "none" : "currentColor"}
      strokeWidth={filled ? 0 : 1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  );
}

function Lucide({ icon: Icon, size = 20, className }: IconProps & { icon: LucideIcon }) {
  return (
    <Icon
      size={size}
      strokeWidth={1.8}
      className={className}
      aria-hidden="true"
      focusable="false"
    />
  );
}

export const PlayIcon = (props: IconProps) => (
  <Svg {...props} filled>
    <path
      d="M8 5.14v13.72a1 1 0 0 0 1.52.85l11.14-6.86a1 1 0 0 0 0-1.7L9.52 4.29A1 1 0 0 0 8 5.14Z"
      transform="translate(-2 0)"
    />
  </Svg>
);

export const PauseIcon = (props: IconProps) => (
  <Svg {...props} filled>
    <rect x="6" y="5" width="4" height="14" rx="1" />
    <rect x="14" y="5" width="4" height="14" rx="1" />
  </Svg>
);

export const PreviousIcon = (props: IconProps) => (
  <Svg {...props} filled>
    <path d="M7 5a1 1 0 0 1 1 1v12a1 1 0 0 1-2 0V6a1 1 0 0 1 1-1Z" />
    <path d="M19 5.5v13a1 1 0 0 1-1.53.85l-9.5-6.5a1 1 0 0 1 0-1.7l9.5-6.5A1 1 0 0 1 19 5.5Z" />
  </Svg>
);

export const NextIcon = (props: IconProps) => (
  <Svg {...props} filled>
    <path d="M17 5a1 1 0 0 1 1 1v12a1 1 0 0 1-2 0V6a1 1 0 0 1 1-1Z" />
    <path d="M5 5.5v13a1 1 0 0 0 1.53.85l9.5-6.5a1 1 0 0 0 0-1.7l-9.5-6.5A1 1 0 0 0 5 5.5Z" />
  </Svg>
);

export const ShuffleIcon = (props: IconProps) => (
  <Svg {...props}>
    <path d="M16 3h5v5" />
    <path d="M4 20 21 3" />
    <path d="M21 16v5h-5" />
    <path d="M15 15l6 6" />
    <path d="M4 4l5 5" />
  </Svg>
);

export const RepeatIcon = (props: IconProps) => (
  <Svg {...props}>
    <path d="M17 2l4 4-4 4" />
    <path d="M3 11v-1a4 4 0 0 1 4-4h14" />
    <path d="M7 22l-4-4 4-4" />
    <path d="M21 13v1a4 4 0 0 1-4 4H3" />
  </Svg>
);

export const RepeatOneIcon = (props: IconProps) => (
  <Svg {...props}>
    <path d="M17 2l4 4-4 4" />
    <path d="M3 11v-1a4 4 0 0 1 4-4h14" />
    <path d="M7 22l-4-4 4-4" />
    <path d="M21 13v1a4 4 0 0 1-4 4H3" />
    <path d="M11 15h2v-4l-1.5 1" />
  </Svg>
);

export const HeartIcon = ({ filled = false, ...props }: IconProps & { filled?: boolean }) => (
  <Svg {...props} filled={filled}>
    <path d="M12 20.5 4.6 13a4.7 4.7 0 0 1 0-6.7 4.7 4.7 0 0 1 6.7 0l.7.7.7-.7a4.7 4.7 0 0 1 6.7 0 4.7 4.7 0 0 1 0 6.7Z" />
  </Svg>
);

export const VolumeIcon = (props: IconProps) => <Lucide {...props} icon={LucideVolume2} />;

export const MuteIcon = (props: IconProps) => <Lucide {...props} icon={LucideVolumeX} />;

export const HomeIcon = (props: IconProps) => <Lucide {...props} icon={LucideHome} />;

export const SearchIcon = (props: IconProps) => <Lucide {...props} icon={LucideSearch} />;

export const LibraryIcon = (props: IconProps) => (
  <Svg {...props}>
    <path d="M4 4v16" />
    <path d="M9 4v16" />
    <path d="M14 5l5 14" />
  </Svg>
);

export const AlbumIcon = (props: IconProps) => (
  <Svg {...props}>
    <circle cx="12" cy="12" r="9" />
    <circle cx="12" cy="12" r="2.5" />
  </Svg>
);

export const ArtistIcon = (props: IconProps) => (
  <Svg {...props}>
    <circle cx="12" cy="8" r="4" />
    <path d="M5 21c0-3.9 3.1-7 7-7s7 3.1 7 7" />
  </Svg>
);

export const ShieldIcon = (props: IconProps) => <Lucide {...props} icon={LucideShield} />;

export const ImageIcon = (props: IconProps) => <Lucide {...props} icon={LucideImage} />;

export const NoteIcon = (props: IconProps) => (
  <Svg {...props}>
    <circle cx="7" cy="18" r="3" />
    <circle cx="18" cy="15.5" r="3" />
    <path d="M10 18V7l11-2.5v11" />
  </Svg>
);

export const PlaylistIcon = (props: IconProps) => (
  <Svg {...props}>
    <path d="M4 7h11" />
    <path d="M4 12h11" />
    <path d="M4 17h7" />
    <circle cx="18" cy="16" r="3" />
    <path d="M21 16V8" />
  </Svg>
);

export const ClockIcon = (props: IconProps) => <Lucide {...props} icon={LucideClock} />;

export const UploadIcon = (props: IconProps) => <Lucide {...props} icon={LucideUpload} />;

export const DownloadIcon = (props: IconProps) => <Lucide {...props} icon={LucideDownload} />;

export const PlusIcon = (props: IconProps) => <Lucide {...props} icon={LucidePlus} />;

export const TrashIcon = (props: IconProps) => <Lucide {...props} icon={LucideTrash} />;

export const MoreIcon = (props: IconProps) => (
  <Svg {...props} filled>
    <circle cx="12" cy="5" r="1.8" />
    <circle cx="12" cy="12" r="1.8" />
    <circle cx="12" cy="19" r="1.8" />
  </Svg>
);

export const CloseIcon = (props: IconProps) => <Lucide {...props} icon={LucideX} />;

export const ChevronDownIcon = (props: IconProps) => <Lucide {...props} icon={LucideChevronDown} />;

export const ChevronUpIcon = (props: IconProps) => <Lucide {...props} icon={LucideChevronUp} />;

export const ChevronLeftIcon = (props: IconProps) => <Lucide {...props} icon={LucideChevronLeft} />;

export const ChevronRightIcon = (props: IconProps) => (
  <Lucide {...props} icon={LucideChevronRight} />
);

export const QueueIcon = (props: IconProps) => (
  <Svg {...props}>
    <path d="M4 6h16" />
    <path d="M4 11h16" />
    <path d="M4 16h9" />
    <path d="M16 19l4-3-4-3Z" />
  </Svg>
);

export const RadioIcon = (props: IconProps) => (
  <Svg {...props}>
    <circle cx="12" cy="12" r="2" />
    <path d="M8.5 15.5a5 5 0 0 1 0-7" />
    <path d="M15.5 8.5a5 5 0 0 1 0 7" />
    <path d="M5.5 18.5a9 9 0 0 1 0-13" />
    <path d="M18.5 5.5a9 9 0 0 1 0 13" />
  </Svg>
);

export const EditIcon = (props: IconProps) => <Lucide {...props} icon={LucidePencil} />;

export const SignOutIcon = (props: IconProps) => <Lucide {...props} icon={LucideLogOut} />;

export const DataSaverIcon = (props: IconProps) => (
  <Svg {...props}>
    <path d="M12 3a9 9 0 1 0 9 9" />
    <path d="M21 3v6h-6" />
    <path d="M12 12 8.5 8.5" />
  </Svg>
);

export const GripIcon = (props: IconProps) => (
  <Svg {...props} filled>
    <circle cx="9" cy="6" r="1.4" />
    <circle cx="15" cy="6" r="1.4" />
    <circle cx="9" cy="12" r="1.4" />
    <circle cx="15" cy="12" r="1.4" />
    <circle cx="9" cy="18" r="1.4" />
    <circle cx="15" cy="18" r="1.4" />
  </Svg>
);

export const WarningIcon = (props: IconProps) => <Lucide {...props} icon={LucideTriangleAlert} />;
